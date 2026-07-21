using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldLifecycleFrameGoldenTests
    {
        [Fact]
        public void SpawnAndExitBodies_MatchOriginalSeatLayouts()
        {
            Assert.Equal(new byte[] { 0, 10 }, FieldLifecycleFrames.Spawn(10));
            Assert.Equal(new byte[] { 2, 10 }, FieldLifecycleFrames.SpawnRejected(10, 2));
            Assert.Equal(new byte[] { 10 }, FieldLifecycleFrames.Exit(10));
        }

        [Fact]
        public void DeathBody_MatchesOriginalFiveByteLayout()
        {
            Assert.Equal(new byte[] { 1, 4, 10, 7, 9 },
                FieldLifecycleFrames.Death(1, 4, 10, 7, 9));
        }

        [Fact]
        public void ExitExperienceBody_IsSignedLittleEndianDword()
        {
            Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 },
                FieldLifecycleFrames.ExitExperience(0x12345678));
        }

        [Fact]
        public void NewRoundFrame_UsesRoundAndMvpSeatsFromOriginalOffsets()
        {
            var field = new Field(1) { Round = 3, LeaderSlotA = 2, LeaderSlotB = 11 };

            Assert.Equal(new byte[] { 0x49, 0, 3, 2, 11 }, field.Build0x49());
        }

        [Fact]
        public void NonBossLeaderOffsetsDefaultToOriginalNoSeatSentinel()
        {
            var field = new Field(1) { Round = 1 };

            Assert.Equal(new byte[] { 0x49, 0, 1, 0x14, 0x14 }, field.Build0x49());
            Assert.Equal(Field.NoSeat, field.LeaderSlotA);
            Assert.Equal(Field.NoSeat, field.LeaderSlotB);
        }

        [Fact]
        public void FieldStatus_UsesNineLogicalBytesAndZeroAesPadding()
        {
            var field = new Field(1)
            {
                Round = 3,
                Wins0 = 1,
                Wins1 = 2,
                LeaderSlotA = 2,
                LeaderSlotB = 11
            };

            Assert.Equal(new byte[] { 0x48, 0, 3, 0xB3, 1, 1, 2, 2, 11, 0, 0, 0 },
                field.Build0x48(0x01B3));
        }

        [Fact]
        public void PvpMatchEnd_UsesOriginalShortReasonFrame()
        {
            var field = new Field(1) { Name = "must-not-leak" };

            Assert.Equal(new byte[] { 0x44, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                field.BuildMatchEnd(5));
        }
    }
}
