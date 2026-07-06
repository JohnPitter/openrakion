using System;
using System.Net.Sockets;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Estado de REDE (infra) de um bot: o socket UDP dedicado (origem dos datagramas sintetizados, via
    /// relay do servidor) + o estado do lockstep de sessão P2P (<see cref="BotLockstep"/>). Vive FORA do
    /// domínio <see cref="Domain.BotPlayer"/>; o <see cref="BotManager"/> cria/descarta por round.
    /// </summary>
    internal sealed class BotNetLink : IDisposable
    {
        /// <summary>Socket UDP dedicado (porta única = origem que identifica o bot no relay do servidor).</summary>
        public Socket? UdpSocket;

        /// <summary>Os 2 opens do lockstep já foram enviados neste round (o canal abre uma vez por round).</summary>
        public bool LockstepOpened;

        /// <summary>Próximo push de sessão periódico (TickCount64), throttle do lado-bot do lockstep.</summary>
        public long NextLockstepPushMs;

        /// <summary>Fecha o socket + zera o lockstep (round-reset / descarte do bot).</summary>
        public void Dispose()
        {
            try { UdpSocket?.Close(); } catch { }
            UdpSocket = null;
            LockstepOpened = false;
        }
    }
}
