using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class ClientSessionLoggingTests
    {
        [Fact]
        public void LoginPayload_IsRedactedFromDebugLog()
        {
            byte[] credentials = { 0x74, 0x65, 0x73, 0x74 };

            string formatted = ClientSession.FormatPayloadForLog(Protocol.Op.Login, credentials);

            Assert.Equal("<4B redacted>", formatted);
            Assert.DoesNotContain("74657374", formatted);
        }

        [Fact]
        public void NonLoginPayload_RemainsAvailableForProtocolDiagnostics()
        {
            Assert.Equal("0102", ClientSession.FormatPayloadForLog(0x47, new byte[] { 1, 2 }));
        }
    }
}
