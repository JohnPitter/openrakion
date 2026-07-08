using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Ciclo de vida do estado de REDE dos bots (socket UDP dedicado) + o LADO DO BOT do lockstep de sessão
    /// P2P (<see cref="BotLockstep"/>) — o handshake que registra o bot como peer no host (colisão/HIT×N).
    ///
    /// TODO tráfego do bot segue a via golden: socket dedicado (41xxx) → servidor (loopback) → UdpGameplay
    /// relaya ao host DO SOCKET DO SERVIDOR (a mesma origem que o 0x319 registrou — regra fixa: nenhum
    /// pacote do bot fala direto com o cliente). O lado do HOST do lockstep (ackear os pushes dele) mora no
    /// <see cref="UdpGameplay"/>, que é onde eles chegam.
    ///
    /// HISTÓRICO: o probe SE1 (mini-peer CONNECT/TAGV) foi REMOVIDO 2026-07-06 — a captura de 2 humanos
    /// provou que esse dialeto NÃO existe no fio (zero TAGV em 3726 frames); o canal real é o
    /// open+push de 12/13B do <see cref="BotLockstep"/>. Ver docs/peer-registration-plan.md.
    /// </summary>
    public sealed partial class BotManager
    {
        private static int _botPortSeq = 41000;   // portas únicas p/ cada bot (acima do range do servidor)

        /// <summary>Cadência do push de sessão do bot (captura: pushes periódicos a partida toda; o host
        /// re-pusha o lado dele a cada ~5s).</summary>
        private const long LockstepPushIntervalMs = 1000;

        /// <summary>Estado de rede (socket + canais de lockstep) por bot — infra FORA do domínio. Criado
        /// on-demand no primeiro spawn; persiste a partida toda (como o socket de um cliente humano) e é
        /// descartado no fim do match / remoção do bot (<see cref="DisposeLink"/>).</summary>
        private readonly ConcurrentDictionary<BotPlayer, BotNetLink> _links = new();

        /// <summary>Link de rede do bot (cria vazio on-demand).</summary>
        private BotNetLink LinkOf(BotPlayer bot) => _links.GetOrAdd(bot, static _ => new BotNetLink());

        /// <summary>Descarta o link de rede do bot (fecha socket + zera lockstep). Chamado no round-reset e no descarte.</summary>
        private void DisposeLink(BotPlayer bot) { if (_links.TryRemove(bot, out var l)) l.Dispose(); }

        /// <summary>
        /// Garante o socket UDP dedicado do bot (origem dos datagramas sintetizados). Idempotente; só ENVIA
        /// ao servidor (loopback), que relaya do próprio socket.
        /// </summary>
        private void EnsureBotUdpSocket(BotPlayer bot)
        {
            var link = LinkOf(bot);
            if (link.UdpSocket != null) return;
            int port = Interlocked.Increment(ref _botPortSeq);
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.Bind(new IPEndPoint(IPAddress.Loopback, port));
            link.UdpSocket = sock;
        }

        /// <summary>
        /// LADO DO BOT do lockstep de sessão, POR HUMANO — o canal 0x0304 é PAREADO (como na captura 2301↔2302),
        /// então o bot abre um canal com CADA humano do stage (dstSeat no byte 7 endereça o par): DOIS opens
        /// (uma vez por partida por par) e push periódico com o seat do bot. Chamado todo BotTick
        /// (self-throttled). Cada humano, ao receber estes frames + o ack correto dos pushes DELE
        /// (UdpGameplay), fecha o canal → peer registrado.
        /// </summary>
        private void EnsureBotLockstep(Domain.Field f, PlayerRec rec, BotPlayer bot, long now)
        {
            EnsureBotUdpSocket(bot);
            var link = LinkOf(bot);
            var sock = link.UdpSocket;
            if (sock == null) return;

            byte botSeat = (byte)rec.Slot;
            var serverEp = new IPEndPoint(IPAddress.Loopback, _gameplayPort());

            ClientSession[] humans;
            lock (f.Players) humans = f.Players.ToArray();
            foreach (var h in humans)
            {
                if (!h.Connected || h.Status != 3) continue;   // só quem já carregou o stage
                var hRec = f.FindRec(h);
                if (hRec == null) continue;
                byte hSeat = (byte)hRec.Slot;
                var ch = link.ChannelTo(hSeat);

                if (!ch.Opened)
                {
                    ch.Opened = true;   // canal abre 1x POR PARTIDA por par (não por round — captura)
                    for (int i = 0; i < 2; i++)   // captura l.12/14: o joiner abre o canal com DOIS opens
                    {
                        uint seq = bot.UdpSeq++;
                        try { sock.SendTo(BotLockstep.BuildOpen(seq, hSeat, BotLockstep.NextToken(seq)), serverEp); } catch { }
                    }
                    Log.Ok("peer", "lockstep OPEN '{0}' seat {1} -> humano seat {2} (canal de sessão P2P)", bot.Name, botSeat, hSeat);
                }
                if (now >= ch.NextPushMs)
                {
                    ch.NextPushMs = now + LockstepPushIntervalMs;
                    uint seq = bot.UdpSeq++;
                    try { sock.SendTo(BotLockstep.BuildPush(seq, botSeat, hSeat, BotLockstep.NextToken(seq)), serverEp); } catch { }
                }
            }
        }
    }
}
