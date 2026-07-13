using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Estado de REDE (infra) de um bot: socket UDP dedicado e presença reliable por humano. Os datagramas
    /// sintetizados são enviados ao servidor em loopback e relayados somente pelas rotas do bot.
    /// Vive FORA do domínio <see cref="Domain.BotPlayer"/>; o <see cref="BotManager"/> cria no primeiro
    /// spawn e descarta no fim do match (o socket persiste a partida toda, como o de um cliente humano).
    /// </summary>
    internal sealed class BotNetLink : IDisposable
    {
        /// <summary>Socket UDP dedicado (porta única = origem que identifica o bot no relay do servidor).</summary>
        public Socket? UdpSocket;

        /// <summary>Estado reliable isolado por par bot→humano. Nunca representa nem roteia um par
        /// humano→humano.</summary>
        public readonly Dictionary<int, BotPeerPresence> PresenceByHumanSeat = new();

        /// <summary>Último beacon 0x830C do próprio bot, broadcast uma vez por cadência.</summary>
        public long LastSelfAnchorMs;

        /// <summary>Token monotônico dos pushes reliable emitidos pelo bot.</summary>
        public uint NextPresenceToken;

        public void ResetPresence()
        {
            PresenceByHumanSeat.Clear();
            LastSelfAnchorMs = 0;
            NextPresenceToken = unchecked((uint)Environment.TickCount64);
        }

        /// <summary>Fecha o socket (fim do match / descarte do bot).</summary>
        public void Dispose()
        {
            try { UdpSocket?.Close(); } catch { }
            UdpSocket = null;
            ResetPresence();
        }
    }

    internal sealed class BotPeerPresence
    {
        public ClientSession? Session;
        public long CreatedAtMs;
        public long LastPushMs;
        public bool AnchorSalvoSent;
    }
}
