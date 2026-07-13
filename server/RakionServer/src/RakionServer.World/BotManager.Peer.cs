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
    /// Ciclo de vida do estado de REDE dos bots: socket UDP dedicado, movimento/ataque unreliable e presença
    /// reliable isolada por par bot→humano.
    ///
    /// TODO tráfego do bot segue a via golden: socket dedicado (41xxx) → servidor (loopback) → UdpGameplay
    /// relaya ao host DO SOCKET DO SERVIDOR (a mesma origem que o 0x319 registrou — regra fixa: nenhum
    /// pacote do bot fala direto com o cliente).
    ///
    /// O servidor nunca relaya nem registra um seat humano para outro: o P2P humano permanece direto. Somente
    /// pacotes originados no socket do bot entram nesta ponte, evitando a dupla-entrega que quebrava o HIT×N.
    /// </summary>
    public sealed partial class BotManager
    {
        private static int _botPortSeq = 41000;   // portas únicas p/ cada bot (acima do range do servidor)
        private const long PresenceEstablishIntervalMs = 1000;
        private const long PresenceSteadyIntervalMs = 5000;
        private const long PresenceEstablishWindowMs = 10000;
        private const long SelfAnchorIntervalMs = 1000;

        /// <summary>Estado de rede (socket) por bot — infra FORA do domínio. Criado
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

        /// <summary>Emite somente o lado bot→humano do canal reliable. Cada push é endereçado a um humano;
        /// âncoras são originadas no seat do bot. Nenhum seat humano é registrado ou relayado para outro.</summary>
        private void EmitBotCombatPresence(Field field, PlayerRec botRecord, BotPlayer bot, long now)
        {
            EnsureBotUdpSocket(bot);
            var link = LinkOf(bot);
            if (link.UdpSocket == null) return;
            var server = new IPEndPoint(IPAddress.Loopback, _gameplayPort());

            foreach (var human in field.Slots)
            {
                if (human.State == 0 || human.IsBot || human.Session?.UdpEndpoint == null) continue;
                var presence = PresenceFor(link, human.Session, human.Slot, now);
                if (!presence.AnchorSalvoSent)
                {
                    presence.AnchorSalvoSent = true;
                    SendAnchorSalvo(link.UdpSocket, server, bot, botRecord.Slot, human.Slot);
                }
                EmitBotPushIfDue(link, presence, bot, botRecord.Slot, human.Slot, server, now);
            }

            if (now - link.LastSelfAnchorMs < SelfAnchorIntervalMs) return;
            link.LastSelfAnchorMs = now;
            SendAnchorSalvo(link.UdpSocket, server, bot, botRecord.Slot, botRecord.Slot);
        }

        private static BotPeerPresence PresenceFor(BotNetLink link, ClientSession session, int humanSeat, long now)
        {
            if (link.PresenceByHumanSeat.TryGetValue(humanSeat, out var presence) &&
                ReferenceEquals(presence.Session, session)) return presence;
            presence = new BotPeerPresence { Session = session, CreatedAtMs = now };
            link.PresenceByHumanSeat[humanSeat] = presence;
            return presence;
        }

        private void EmitBotPushIfDue(BotNetLink link, BotPeerPresence presence, BotPlayer bot,
            int botSeat, int humanSeat, IPEndPoint server, long now)
        {
            long age = now - presence.CreatedAtMs;
            long interval = age < PresenceEstablishWindowMs
                ? PresenceEstablishIntervalMs : PresenceSteadyIntervalMs;
            if (now - presence.LastPushMs < interval) return;

            presence.LastPushMs = now;
            uint token = ++link.NextPresenceToken;
            byte[] packet = BotLockstep.BuildPush(bot.UdpSeq++, (byte)botSeat, (byte)humanSeat, token);
            try { link.UdpSocket!.SendTo(packet, server); } catch { }
        }

        private static void SendAnchorSalvo(Socket socket, IPEndPoint server, BotPlayer bot,
            int botSeat, int targetSeat)
        {
            for (int copy = 0; copy < 2; copy++)
            {
                byte[] packet = BotMovement.BuildCombatAnchorDatagram(bot, botSeat, targetSeat);
                try { socket.SendTo(packet, server); } catch { }
            }
        }

        /// <summary>Fecha, em nome do bot, uma mensagem reliable recebida do humano.</summary>
        internal byte[] BuildReliableAck(BotPlayer bot, int botSeat, uint confirmedSequence) =>
            BotLockstep.BuildReliableAck(bot.UdpSeq++, (byte)botSeat, confirmedSequence);
    }
}
