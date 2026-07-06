using System;
using System.Net;
using System.Net.Sockets;
using RakionServer.Peer;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Estado de REDE (infra) de um bot: o socket UDP dedicado + o peer de netcode (mini-peer que fala o
    /// handshake de sessão SE1 com o HOST, p/ registrar o bot como peer real → colisão/HIT×N nativos). Vive
    /// FORA do domínio <see cref="Domain.BotPlayer"/>; o <see cref="BotManager"/> cria/descarta por round.
    /// </summary>
    internal sealed class BotNetLink : IDisposable
    {
        /// <summary>Socket UDP dedicado (porta única = origem que identifica o bot; fala com o host no probe do peer).</summary>
        public Socket? UdpSocket;

        /// <summary>Peer de netcode (mini-peer): handshake CONNECT/CRC/ADDPLAYER com o host. null fora do modo peer.</summary>
        public BotPeer? Peer;

        /// <summary>Endpoint do bot (IP:porta do socket dedicado).</summary>
        public IPEndPoint? BotEndpoint;

        /// <summary>O host engajou o handshake (respondeu além do ack)? Diagnóstico do make-or-break.</summary>
        public bool HostEngaged;

        /// <summary>Fecha o socket + larga o peer (round-reset / descarte do bot).</summary>
        public void Dispose()
        {
            Peer = null;
            try { UdpSocket?.Close(); } catch { }
            UdpSocket = null;
            BotEndpoint = null;
        }
    }
}
