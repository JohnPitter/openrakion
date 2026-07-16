using System;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldVoteFrameGoldenTests
    {
        [Fact]
        public void OpenBody_MatchesWorldSerializer()
        {
            Assert.Equal("0141464b00",
                Convert.ToHexString(FieldVoteFrames.OpenBody(1, "AFK")).ToLowerInvariant());
        }

        [Fact]
        public void Status_MatchesTargetedLobbyResponse()
        {
            Assert.Equal("5f0007",
                Convert.ToHexString(FieldVoteFrames.Status(FieldVoteStatus.MasterOnly)).ToLowerInvariant());
        }

        [Fact]
        public void ResultBody_MatchesNineByteFieldPacketBody()
        {
            var result = new FieldVoteFinal(0, 3, 2, 0, 0, 1, true);

            Assert.Equal("00000302000001",
                Convert.ToHexString(FieldVoteFrames.ResultBody(result)).ToLowerInvariant());
        }
    }
}
