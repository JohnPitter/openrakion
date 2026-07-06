using System;
using System.Net;
using System.Net.Sockets;
using RakionServer.Peer;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Estado de REDE (infra) de um bot: o peer de netcode (handshake que abre o gate do 0x30a) + o socket UDP
    /// dedicado (porta única = endpoint distinto p/ o host). Vive FORA do domínio <see cref="Domain.BotPlayer"/>,
    /// que fica puro (posição/HP/IA); o <see cref="BotManager"/> cria/descarta o link por round (mapa keyed pelo bot).
    /// </summary>
    internal sealed class BotNetLink : IDisposable
    {
        /// <summary>Peer de netcode (slice RakionServer.Peer): fala o handshake REQ_CONNECTREMOTE→…→SEQ_ADDPLAYER
        /// p/ REGISTRAR o bot como peer e ABRIR o gate do 0x30a. null = ainda não criado (novo round/spawn).</summary>
        public BotPeer? Peer;

        /// <summary>Socket UDP dedicado (porta única = endpoint único para o host).</summary>
        public Socket? UdpSocket;

        /// <summary>Endpoint do bot (IP:porta do socket dedicado).</summary>
        public IPEndPoint? BotEndpoint;

        /// <summary>True se o gate do 0x30a abriu (o host emitiu gameplay). Encapsula o <see cref="Peer"/>.</summary>
        public bool GateOpen => Peer?.GateOpen ?? false;

        /// <summary>Força o gate aberto (fallback): se o handshake não completou, emite frames direto.</summary>
        public void ForceGateOpen() { if (Peer is { GateOpen: false } p) p.ForceGate(); }

        /// <summary>Fecha o socket e larga o peer (round-reset / descarte do bot).</summary>
        public void Dispose()
        {
            Peer = null;
            try { UdpSocket?.Close(); } catch { }
            UdpSocket = null;
            BotEndpoint = null;
        }
    }
}
