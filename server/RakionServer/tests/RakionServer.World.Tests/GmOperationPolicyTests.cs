using System.Collections.Generic;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public class GmOperationPolicyTests
    {
        private static readonly IReadOnlySet<string> Allowed =
            new HashSet<string> { "192.168.1.6" };

        [Fact]
        public void WrongSubStatusMatchesOriginalDisconnect()
        {
            Assert.Equal<byte?>(0xB9, GmOperationPolicy.DisconnectReason(
                0x04, "192.168.1.6", Allowed));
        }

        [Fact]
        public void DeniedIpMatchesOriginalDisconnect()
        {
            Assert.Equal<byte?>(0xBA, GmOperationPolicy.DisconnectReason(
                0x34, "127.0.0.1", Allowed));
        }

        [Fact]
        public void AllowedIpCompletesWithoutResponseOrDisconnect()
        {
            Assert.Null(GmOperationPolicy.DisconnectReason(
                0x34, "192.168.1.6", Allowed));
        }
    }
}
