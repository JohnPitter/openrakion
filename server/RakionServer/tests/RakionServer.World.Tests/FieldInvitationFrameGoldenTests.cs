using System;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldInvitationFrameGoldenTests
    {
        [Fact]
        public void Notification_MatchesWorldFieldSerializer()
        {
            var field = new Field(7)
            {
                MapId = 1,
                Mode = 3,
                MinLevel = 1,
                MaxLevel = 99,
                LevelRangeCode = 0,
                MaxRounds = 1,
                RoundDurationSec = 432,
                Name = "RECombat",
                Description = "battle"
            };

            byte[] frame = FieldInvitationFrames.Notification(9, "JP", 7, field);

            Assert.Equal(
                "720009004a50000700010301630001b0015245436f6d62617400626174746c6500",
                Convert.ToHexString(frame).ToLowerInvariant());
        }
    }
}
