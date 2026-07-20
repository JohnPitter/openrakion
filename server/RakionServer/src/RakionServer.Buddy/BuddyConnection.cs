using System.Net;
using System.Net.Sockets;
using System.Threading;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    internal sealed class BuddyConnection
    {
        public BuddyConnection(Socket socket, string remoteIp)
        {
            Socket = socket;
            RemoteIp = remoteIp;
        }

        public Socket Socket { get; }
        public string RemoteIp { get; }
        public uint CredentialSeed { get; set; }
        public uint CredentialCookie { get; set; }
        public string AccountId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ActiveCharacterName { get; set; } = "";
        public string PendingProfileSignature { get; set; } = "";
        public long PendingProfileSince { get; set; }
        public uint UdpToken { get; set; }
        public IPEndPoint? UdpEndpoint { get; set; }
        public long TunnelWindowStart { get; set; }
        public int TunnelPackets { get; set; }
        public PacketCrypto Crypto { get; set; } = new();
        public ChatSessionState ChatState { get; } = new();
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public bool Authenticated => AccountId.Length > 0 && Crypto.Enabled;
    }
}
