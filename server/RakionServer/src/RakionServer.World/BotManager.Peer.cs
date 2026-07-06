using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Ciclo de vida do estado de REDE dos bots (<see cref="BotNetLink"/>: socket UDP dedicado). Cada bot
    /// emite os datagramas sintetizados de um socket em porta única (>41000) rumo ao SERVIDOR (loopback);
    /// o <see cref="Network.UdpGameplay"/> desambigua o dono pelo srcSlot e relaya ao host do PRÓPRIO
    /// socket do servidor — a mesma origem que o 0x319 registra no cliente.
    ///
    /// REGRA (regressão 2026-07: bot congelado): NENHUM pacote do bot vai DIRETO ao cliente. O gate de
    /// movimento do cliente (IsValidUDP_ForPlayer @0x36109da0) valida a ORIGEM (IP,porta) do 0x30a contra
    /// o endpoint registrado pro slot — que o 0x319 do servidor fixa no socket do UdpGameplay. O antigo
    /// mini-peer (BotPeer.Connect: 0x0304 open + keepalives, do socket 41xxx direto ao host) re-ligava o
    /// peer do slot ao endpoint ERRADO → todos os 0x30a relayados eram rejeitados → bot parado no spawn.
    /// O handshake que o cliente REALMENTE exige (eco de lockstep 0x0305 pro 0x0304 dele) é respondido
    /// pelo UdpGameplay, do socket do servidor. O codec de peer (RakionServer.Peer) segue vivo p/ o
    /// sub-projeto headless-H3 — só não fala mais com o cliente daqui.
    /// </summary>
    public sealed partial class BotManager
    {
        private static int _botPortSeq = 41000;   // portas únicas p/ cada bot (acima do range do servidor)

        /// <summary>Estado de rede (socket) por bot — infra FORA do domínio. Criado on-demand; descartado no
        /// round-reset (<see cref="DisposeLink"/> no spawn) e no descarte do bot.</summary>
        private readonly ConcurrentDictionary<BotPlayer, BotNetLink> _links = new();

        /// <summary>Link de rede do bot (cria vazio on-demand).</summary>
        private BotNetLink LinkOf(BotPlayer bot) => _links.GetOrAdd(bot, static _ => new BotNetLink());

        /// <summary>Descarta o link de rede do bot (fecha socket). Chamado no round-reset (novo spawn)
        /// e ao remover/descartar o bot. Sem-op se não existe.</summary>
        private void DisposeLink(BotPlayer bot) { if (_links.TryRemove(bot, out var l)) l.Dispose(); }

        /// <summary>
        /// Garante o socket UDP dedicado do bot (origem dos datagramas sintetizados). Idempotente; não
        /// depende do endpoint do host — o socket só ENVIA ao servidor (loopback), nunca ao cliente.
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
    }
}
