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
    /// Ciclo de vida do estado de REDE dos bots + o PROBE do peer real (make-or-break do HIT×N nativo).
    ///
    /// O socket dedicado (porta >41000) do bot SEMPRE serve os datagramas type-7 (0x30a via relay do servidor).
    /// Quando <see cref="Network.BotMovement.BotPeerProbe"/> está ligado, o bot TAMBÉM tenta o handshake de
    /// sessão SE1 DIRETO com o host (mini-peer <see cref="PeerLib.BotPeer"/>): manda o CONNECT ao endpoint P2P
    /// do host e escuta a resposta. O objetivo do probe é RESPONDER, com log classificado, à pergunta:
    /// <b>o host ENGAJA o CONNECT do bot (responde com o próprio push reliable role=0x0a) ou só ACKeia
    /// (0x0305) e ignora?</b> — a verdade de referência (docs/p2p-handshake-groundtruth.txt) mostra o host
    /// respondendo a um 2º humano; se ele NÃO responde ao bot, o lever é o modo networked-server (join do
    /// broker), não o handshake em si. Ver docs/peer-registration-plan.md.
    ///
    /// NOTA: no probe o 0x0304 do peer (do socket 41xxx) re-liga o endpoint do slot no cliente → o gate do
    /// 0x30a type-7 quebra e o bot CONGELA. É esperado: o probe é DIAGNÓSTICO (lê-se o log), não o caminho final.
    /// </summary>
    public sealed partial class BotManager
    {
        private const string BotPeerModName = "";
        private const uint BotPeerFileCrcSentinel = 0x12345678;

        private static int _botPortSeq = 41000;   // portas únicas p/ cada bot (acima do range do servidor)

        /// <summary>Estado de rede (socket + peer) por bot — infra FORA do domínio. Criado on-demand; descartado
        /// no round-reset (<see cref="DisposeLink"/> no spawn) e no descarte do bot.</summary>
        private readonly ConcurrentDictionary<BotPlayer, BotNetLink> _links = new();

        /// <summary>Link de rede do bot (cria vazio on-demand).</summary>
        private BotNetLink LinkOf(BotPlayer bot) => _links.GetOrAdd(bot, static _ => new BotNetLink());

        /// <summary>Descarta o link de rede do bot (fecha socket + larga peer). Chamado no round-reset e no descarte.</summary>
        private void DisposeLink(BotPlayer bot) { if (_links.TryRemove(bot, out var l)) l.Dispose(); }

        /// <summary>
        /// Garante o socket UDP dedicado do bot (origem dos datagramas type-7). Idempotente; só ENVIA ao
        /// servidor (loopback). O probe do peer (<see cref="EnsureBotPeerProbe"/>) reusa o mesmo socket.
        /// </summary>
        private void EnsureBotUdpSocket(BotPlayer bot)
        {
            var link = LinkOf(bot);
            if (link.UdpSocket != null) return;
            int port = Interlocked.Increment(ref _botPortSeq);
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.Bind(new IPEndPoint(IPAddress.Loopback, port));
            link.UdpSocket = sock;
            link.BotEndpoint = new IPEndPoint(IPAddress.Loopback, port);
        }

        /// <summary>
        /// PROBE do peer: cria o mini-peer e manda o handshake CONNECT ao endpoint P2P do host, escutando a
        /// resposta com log CLASSIFICADO. Idempotente (1 peer por bot/round). Sem-op se o host ainda não tem
        /// endpoint UDP conhecido.
        /// </summary>
        private void EnsureBotPeerProbe(Domain.Field f, PlayerRec rec, BotPlayer bot)
        {
            var host = f.Master;
            var hostEp = host?.UdpEndpoint;
            if (hostEp == null) { Log.Ok("peer", "probe '{0}': host sem endpoint UDP ainda — aguardando", bot.Name); return; }

            EnsureBotUdpSocket(bot);
            var link = LinkOf(bot);
            var sock = link.UdpSocket!;
            if (link.Peer == null)
            {
                Log.Ok("peer", "PROBE '{0}': socket {1} -> CONNECT ao host P2P {2}", bot.Name, link.BotEndpoint?.ToString() ?? "?", hostEp);
                var character = PeerLib.BotCharacterFactory.ForBot(bot.Name, bot.CharClass, bot.Level, bot.Team);
                var identity = new PeerLib.PeerIdentity(character, rec.Slot);
                link.Peer = new PeerLib.BotPeer(
                    send: pkt => { try { Log.Ok("peer", "  TX->host {0}B [{1}]", pkt.Length, Convert.ToHexString(pkt)); sock.SendTo(pkt, hostEp); } catch { } },
                    identity: identity,
                    modName: BotPeerModName,
                    fileCrcOf: _ => BotPeerFileCrcSentinel);
                _ = Task.Run(() => BotPeerRecvLoop(sock, bot, link));
            }
            if (!link.Peer.Started)
            {
                link.Peer.Connect();
                Log.Ok("peer", "handshake iniciado '{0}' -> host {1} (aguardando ENGAJAMENTO do host)", bot.Name, hostEp);
            }
        }

        /// <summary>Loop de recepção do probe: classifica CADA frame do host — <b>ENGAJA</b> (0x0304 push reliable,
        /// especialmente role=0x0a) vs <b>ack só</b> (0x0305) vs disconnect — e roteia ao <see cref="PeerLib.BotPeer"/>.</summary>
        private async Task BotPeerRecvLoop(Socket sock, BotPlayer bot, BotNetLink link)
        {
            var buf = new byte[1024];
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (sock.IsBound)
            {
                SocketReceiveFromResult res;
                try { res = await sock.ReceiveFromAsync(buf, SocketFlags.None, any); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { continue; }
                catch { break; }
                int n = res.ReceivedBytes;
                if (n <= 0) continue;
                var pkt = new byte[n];
                Buffer.BlockCopy(buf, 0, pkt, 0, n);

                ushort mt = n >= 2 ? (ushort)(pkt[0] | (pkt[1] << 8)) : (ushort)0;
                string verdict = ClassifyHostFrame(pkt);
                Log.Ok("peer", "RX<-host {0} {1}B tipo=0x{2:X4} de {3} [{4}]", verdict, n, mt, res.RemoteEndPoint, Convert.ToHexString(pkt));
                if (verdict.StartsWith("ENGAJOU") && !link.HostEngaged)
                {
                    link.HostEngaged = true;
                    Log.Ok("peer", "*** MAKE-OR-BREAK: HOST ENGAJOU o CONNECT do bot '{0}' — peer VIÁVEL! ***", bot.Name);
                }
                try { link.Peer?.OnDatagram(pkt); } catch (Exception ex) { Log.Debug("peer", "OnDatagram: {0}", ex.Message); }
                if (link.Peer?.GateOpen == true)
                    Log.Ok("peer", "*** GATE ABERTO '{0}' — host aceitou como peer (0x30a nativo)! ***", bot.Name);
            }
        }

        /// <summary>Classifica um frame vindo do host no probe: role=0x0a num 0x0304 = ENGAJOU (o host abriu o
        /// próprio stream de sessão); 0x0304 role=0xff = abriu canal; 0x0305 = só ACK de transporte; INF_DISCONNECTED.</summary>
        private static string ClassifyHostFrame(byte[] pkt)
        {
            if (pkt.Length >= 2 && pkt[0] == 0x04 && pkt[1] == 0x03)
            {
                byte role = pkt.Length >= 8 ? pkt[7] : (byte)0xff;   // [op2][seq4][role@6? ] — role no offset do push
                return role == 0x0a ? "ENGAJOU(0x0304 role=0x0a)" : "abriu-canal(0x0304 role=0xff)";
            }
            if (pkt.Length >= 2 && pkt[0] == 0x05 && pkt[1] == 0x03) return "ack-só(0x0305)";
            if (pkt.Length >= 2 && pkt[0] == 0x0a && pkt[1] == 0x03) return "ENGAJOU(0x030a gameplay)";
            return "outro";
        }
    }
}
