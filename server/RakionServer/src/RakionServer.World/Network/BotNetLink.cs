using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Estado de REDE (infra) de um bot: o socket UDP dedicado (origem dos datagramas sintetizados, via
    /// relay do servidor) + os canais de lockstep de sessão P2P por humano (<see cref="BotLockstep"/>).
    /// Vive FORA do domínio <see cref="Domain.BotPlayer"/>; o <see cref="BotManager"/> cria no primeiro
    /// spawn e descarta no fim do match (o socket persiste a partida toda, como o de um cliente humano).
    /// </summary>
    internal sealed class BotNetLink : IDisposable
    {
        /// <summary>Socket UDP dedicado (porta única = origem que identifica o bot no relay do servidor).</summary>
        public Socket? UdpSocket;

        /// <summary>Canal de sessão 0x0304 com UM humano (pareado, como na captura): aberto 1x por partida
        /// por par + throttle do push periódico.</summary>
        public sealed class Channel
        {
            public bool Opened;
            public long NextPushMs;
        }

        private readonly Dictionary<byte, Channel> _channels = new();

        /// <summary>Canal do bot com o humano do <paramref name="seat"/> (cria vazio on-demand).
        /// Só tocado pelo BotTick (single-thread por field).</summary>
        public Channel ChannelTo(byte seat)
        {
            if (!_channels.TryGetValue(seat, out var ch)) _channels[seat] = ch = new Channel();
            return ch;
        }

        /// <summary>Fecha o socket e zera os canais (fim do match / descarte do bot).</summary>
        public void Dispose()
        {
            try { UdpSocket?.Close(); } catch { }
            UdpSocket = null;
            _channels.Clear();
        }
    }
}
