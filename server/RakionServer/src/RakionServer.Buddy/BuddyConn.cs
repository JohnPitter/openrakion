using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Estado de uma conexão TCP do messenger. A identidade (account/nick) é resolvida por IP no login — o login
    /// do Buddy é cifrado e opaco, então o World grava a <c>messenger_session</c> e o Buddy a lê. O endpoint P2P
    /// (<see cref="UdpEndpoint"/>, p/ onde os amigos abrem o P2P direto do PM) é aprendido pelo echo UDP do
    /// cliente, correlacionado por IP (o RET_LOGIN não carrega token — carregava e o cliente o lia como count).
    /// Os sends são serializados por <see cref="SendLock"/> (a presença, disparada por outra conexão, concorre
    /// com a resposta do próprio loop).
    /// </summary>
    internal sealed class BuddyConn
    {
        public Socket Sock { get; }
        public string Ip { get; }
        public readonly object SendLock = new();

        public BuddyConn(Socket sock, string ip) { Sock = sock; Ip = ip; }

        public string Account = "";
        public string Nick = "";
        public bool LoggedIn;
        public IPEndPoint? UdpEndpoint;                       // endpoint P2P aprendido (echo UDP, correlato por IP)
        public IReadOnlyList<string> BuddyNicks = Array.Empty<string>();   // nicks dos amigos (presença/PM)
    }
}
