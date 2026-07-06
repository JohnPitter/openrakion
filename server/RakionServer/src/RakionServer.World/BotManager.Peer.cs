using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using PeerLib = RakionServer.Peer;

namespace RakionServer.World
{
    /// <summary>
    /// Ciclo de vida do estado de REDE dos bots (<see cref="BotNetLink"/>: socket UDP dedicado + peer de netcode).
    /// Cada bot recebe um SOCKET UDP DEDICADO em porta única — o host vê cada bot como um peer distinto (IP:porta
    /// diferente do servidor). O handshake (REQ_CONNECTREMOTE→…→CRC→SEQ_ADDPLAYER) sai deste socket; o host
    /// registra o endpoint como peer e aceita 0x30a como gameplay válido. O link mora AQUI (mapa keyed pelo bot),
    /// não no domínio <see cref="BotPlayer"/> — que fica puro.
    /// </summary>
    public sealed partial class BotManager
    {
        private const string BotPeerModName = "";
        private const uint BotPeerFileCrcSentinel = 0x12345678;

        private static int _botPortSeq = 41000;   // portas únicas p/ cada bot (acima do range do servidor)

        /// <summary>Estado de rede (socket/peer) por bot — infra FORA do domínio. Criado on-demand; descartado no
        /// round-reset (<see cref="DisposeLink"/> no spawn) e no descarte do bot.</summary>
        private readonly ConcurrentDictionary<BotPlayer, BotNetLink> _links = new();

        /// <summary>Link de rede do bot (cria vazio on-demand).</summary>
        private BotNetLink LinkOf(BotPlayer bot) => _links.GetOrAdd(bot, static _ => new BotNetLink());

        /// <summary>Descarta o link de rede do bot (fecha socket + larga peer). Chamado no round-reset (novo spawn)
        /// e ao remover/descartar o bot. Sem-op se não existe.</summary>
        private void DisposeLink(BotPlayer bot) { if (_links.TryRemove(bot, out var l)) l.Dispose(); }

        // --- acessores de diagnóstico p/ o "/addbot status|debug" (handler fora do BotManager) ---

        /// <summary>Gate do 0x30a do bot aberto? (diagnóstico do status).</summary>
        public bool BotGateOpen(BotPlayer bot) => _links.TryGetValue(bot, out var l) && l.GateOpen;

        /// <summary>Estado do handshake do peer do bot (diagnóstico do status).</summary>
        public string BotHandshake(BotPlayer bot) =>
            (_links.TryGetValue(bot, out var l) ? l.Peer?.HandshakeState.ToString() : null) ?? "no-peer";

        /// <summary>Força o gate do bot aberto (debug "/addbot debug"). Sem-op se não há link/peer.</summary>
        public void ForceBotGate(BotPlayer bot) { if (_links.TryGetValue(bot, out var l)) l.ForceGateOpen(); }

        private void EnsureBotPeerConnected(Domain.Field f, PlayerRec rec, BotPlayer bot)
        {
            // MODO NPC (0x307): o bot é uma entidade NPC, NÃO um peer-jogador. O handshake mini-peer (CONNECT
            // 0x0304 reliable) abre uma "conexão" meio-estabelecida no cliente pro slot do bot e cria um 2º socket
            // (conflito com o socket NPC) — corrompe o estado do canal reliable → o create 0x307 é descartado
            // (render INCONSISTENTE). No modo NPC o peer é irrelevante: guarda dura contra qualquer disparo espúrio.
            if (Network.BotMovement.UseNpcAvatar) return;

            var host = f.Master;
            var hostEp = host?.UdpEndpoint;
            if (hostEp == null) return;

            var link = LinkOf(bot);
            if (link.Peer == null)
            {
                // Socket UDP dedicado p/ este bot — porta única = endpoint único para o host.
                int port = Interlocked.Increment(ref _botPortSeq);
                var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sock.Bind(new IPEndPoint(IPAddress.Loopback, port));
                var botEp = new IPEndPoint(IPAddress.Loopback, port);

                Log.Ok("peer", "bot '{0}' socket UDP porta {1} -> host {2}", bot.Name, port, hostEp);

                var character = PeerLib.BotCharacterFactory.ForBot(bot.Name, bot.CharClass, bot.Level, bot.Team);
                var identity = new PeerLib.PeerIdentity(character, rec.Slot);
                link.Peer = new PeerLib.BotPeer(
                    send: pkt => { try { sock.SendTo(pkt, hostEp); } catch { } },
                    identity: identity,
                    modName: BotPeerModName,
                    fileCrcOf: _ => BotPeerFileCrcSentinel);
                link.UdpSocket = sock;
                link.BotEndpoint = botEp;

                // Loop de recepção: recebe respostas do host e roteia ao BotPeer (via o próprio link).
                _ = Task.Run(() => BotRecvLoop(sock, bot, link));
            }
            if (!link.Peer.Started)
            {
                link.Peer.Connect();
                Log.Ok("peer", "handshake iniciado bot '{0}' -> host {1}", bot.Name, hostEp);
            }
        }

        private async Task BotRecvLoop(Socket sock, BotPlayer bot, BotNetLink link)
        {
            var buf = new byte[1024];
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!bot.Dead && sock.IsBound)
            {
                try
                {
                    var res = await sock.ReceiveFromAsync(buf, SocketFlags.None, any);
                    int n = res.ReceivedBytes;
                    if (n <= 0) continue;
                    var pkt = new byte[n];
                    Buffer.BlockCopy(buf, 0, pkt, 0, n);

                    ushort msgType = n >= 2 ? (ushort)(pkt[0] | (pkt[1] << 8)) : (ushort)0;
                    Log.Debug("peer", "bot '{0}' RX {1}B tipo=0x{2:X4}", bot.Name, n, msgType);

                    link.Peer?.OnDatagram(pkt);

                    if (link.Peer?.GateOpen == true && bot.UdpSeq <= 1)
                        Log.Ok("peer", "GATE ABERTO bot '{0}' — host aceitou como peer!", bot.Name);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { continue; }
                catch { break; }
            }
        }
    }
}
