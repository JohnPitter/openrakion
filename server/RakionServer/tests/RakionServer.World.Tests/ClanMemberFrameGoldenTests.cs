using System.Collections.Generic;
using RakionServer.World.Database;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class ClanMemberFrameGoldenTests
    {
        [Fact]
        public void SuccessContainsCountAndAccountBuddyPairs()
        {
            var members = new List<ClanMemberIdentity>
            {
                new("alice", "ally"),
                new("bob", "b")
            };

            Assert.Equal(new byte[]
            {
                0x78, 0x00, 0x00, 0x02, 0x00,
                (byte)'a', (byte)'l', (byte)'i', (byte)'c', (byte)'e', 0,
                (byte)'a', (byte)'l', (byte)'l', (byte)'y', 0,
                (byte)'b', (byte)'o', (byte)'b', 0,
                (byte)'b', 0
            }, LobbyFrames.ClanMembers(members));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void EmptyAndDatabaseFailureAreShortStatusFrames(byte status)
        {
            Assert.Equal(new byte[] { 0x78, 0x00, status },
                LobbyFrames.ClanMembersStatus(status));
        }
    }
}
