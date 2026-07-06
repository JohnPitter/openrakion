using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Estado de uma conexão TCP do messenger. A identidade (account/nick) é resolvida por IP no login — o login
    /// do Buddy é cifrado e opaco, então o World grava a <c>messenger_session</c> e o Buddy a lê. O token é
    /// ecoado pelo cliente via UDP p/ o brokering P2P aprender o <see cref="UdpEndpoint"/> (endereço para o qual
    /// os amigos abrem o P2P direto). Os sends são serializados por <see cref="SendLock"/> (a presença, disparada
    /// por outra conexão, concorre com a resposta do próprio loop).
    /// </summary>
    internal sealed class BuddyConn
    {
        public Socket Sock { get; }
        public string Ip { get; }
        public int Port { get; }                              // porta TCP de origem -> desambigua 2+ clientes no mesmo IP
        public readonly object SendLock = new();

        public BuddyConn(Socket sock, string ip, int port) { Sock = sock; Ip = ip; Port = port; }

        public string Account = "";
        public string Nick = "";
        public ushort Token;
        public bool LoggedIn;
        public IPEndPoint? UdpEndpoint;                       // endpoint P2P aprendido (token echo via UDP)
        public IReadOnlyList<string> BuddyNicks = Array.Empty<string>();   // nicks dos amigos (presença/PM)
        public string BuddyListSig = "";                      // assinatura conta:nick da lista -> detecta add/remove/nick p/ re-emitir
    }
}
