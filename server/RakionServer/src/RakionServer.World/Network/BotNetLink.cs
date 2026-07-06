using System;
using System.Net.Sockets;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Estado de REDE (infra) de um bot: o socket UDP dedicado de onde os datagramas sintetizados
    /// (0x30a/0x030f/0x0311) partem rumo ao servidor (loopback), que os relaya ao host. A porta única
    /// desambigua o dono no <see cref="UdpGameplay"/> (bot ≠ humano no mesmo 127.0.0.1). Vive FORA do
    /// domínio <see cref="Domain.BotPlayer"/>, que fica puro (posição/HP/IA); o <see cref="BotManager"/>
    /// cria/descarta o link por round (mapa keyed pelo bot).
    ///
    /// NÃO há peer/handshake com o cliente: o gate de movimento do cliente abre por 0x319 + eco de
    /// lockstep 0x0305, ambos emitidos pelo SOCKET DO SERVIDOR (UdpGameplay). Falar com o cliente de um
    /// segundo endpoint re-liga o peer do slot ao endpoint errado e o 0x30a relayado passa a ser
    /// rejeitado (IsValidUDP_ForPlayer) — foi a regressão que congelou o bot (ver bot-movement-status).
    /// </summary>
    internal sealed class BotNetLink : IDisposable
    {
        /// <summary>Socket UDP dedicado (porta única = origem que identifica o bot no relay).</summary>
        public Socket? UdpSocket;

        /// <summary>Fecha o socket (round-reset / descarte do bot).</summary>
        public void Dispose()
        {
            try { UdpSocket?.Close(); } catch { }
            UdpSocket = null;
        }
    }
}
