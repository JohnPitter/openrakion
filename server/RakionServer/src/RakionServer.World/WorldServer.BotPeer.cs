using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Domain;
using PeerLib = RakionServer.Peer;

namespace RakionServer.World
{
    /// <summary>
    /// Ciclo de vida do PEER de netcode dos bots. Cada bot recebe um SOCKET UDP DEDICADO
    /// em porta única — o host vê cada bot como um peer distinto (IP:porta diferente do servidor).
    /// O handshake (REQ_CONNECTREMOTE→…→CRC→SEQ_ADDPLAYER) sai deste socket; o host registra
    /// o endpoint como peer e aceita 0x30a como gameplay válido.
    /// </summary>
    public sealed partial class WorldServer
    {
        private const string BotPeerModName = "";
        private const uint BotPeerFileCrcSentinel = 0x12345678;

        private static int _botPortSeq = 41000;   // portas únicas p/ cada bot (acima do range do servidor)

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

            if (bot.Peer == null)
            {
                // Socket UDP dedicado p/ este bot — porta única = endpoint único para o host.
                int port = Interlocked.Increment(ref _botPortSeq);
                var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sock.Bind(new IPEndPoint(IPAddress.Loopback, port));
                var botEp = new IPEndPoint(IPAddress.Loopback, port);

                Log.Ok("peer", "bot '{0}' socket UDP porta {1} -> host {2}", bot.Name, port, hostEp);

                var character = PeerLib.BotCharacterFactory.ForBot(bot.Name, bot.CharClass, bot.Level, bot.Team);
                var identity = new PeerLib.PeerIdentity(character, rec.Slot);
                bot.Peer = new PeerLib.BotPeer(
                    send: pkt => { try { sock.SendTo(pkt, hostEp); } catch { } },
                    identity: identity,
                    modName: BotPeerModName,
                    fileCrcOf: _ => BotPeerFileCrcSentinel);
                bot.UdpSocket = sock;
                bot.BotEndpoint = botEp;

                // Loop de recepção: recebe respostas do host e roteia ao BotPeer.
                _ = Task.Run(() => BotRecvLoop(sock, bot, f));
            }
            if (!bot.Peer.Started)
            {
                bot.Peer.Connect();
                Log.Ok("peer", "handshake iniciado bot '{0}' -> host {1}", bot.Name, hostEp);
            }
        }

        private async Task BotRecvLoop(Socket sock, BotPlayer bot, Domain.Field f)
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

                    bot.Peer?.OnDatagram(pkt);

                    if (bot.Peer?.GateOpen == true && bot.UdpSeq <= 1)
                        Log.Ok("peer", "GATE ABERTO bot '{0}' — host aceitou como peer!", bot.Name);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { continue; }
                catch { break; }
            }
        }
    }
}
