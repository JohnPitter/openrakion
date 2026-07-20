using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class WorldRequestGatePolicyTests
    {
        [Theory]
        [InlineData(0x0E, 1, -1, 2)]
        [InlineData(0x14, 1, 0, 2)]
        [InlineData(0x2C, 1, 7, 2)]
        [InlineData(0x3A, 1, 7, 2)]
        [InlineData(0x6B, 1, 7, 4)]
        public void AllowsOriginalIdentityAndPhase(
            ushort opcode, int gameInfoId, int characterId, byte status)
        {
            Assert.True(WorldRequestGatePolicy.Evaluate(
                opcode, gameInfoId, characterId, status).Allowed);
        }

        [Theory]
        [InlineData(0x0E, 1, 7, 2, 0x16)]
        [InlineData(0x2C, 0, 7, 2, 0x32)]
        [InlineData(0x2C, 1, 7, 3, 0x33)]
        [InlineData(0x3A, 1, 7, 3, 0x51)]
        [InlineData(0x4B, 1, 7, 2, 0x87)]
        [InlineData(0x73, 1, 0, 2, 0xDE)]
        public void ReturnsOriginalDisconnectReason(
            ushort opcode, int gameInfoId, int characterId, byte status, ushort reason)
        {
            RequestGateResult result = WorldRequestGatePolicy.Evaluate(
                opcode, gameInfoId, characterId, status);

            Assert.Equal(RequestGateAction.Disconnect, result.Action);
            Assert.Equal(reason, result.Code);
        }

        [Fact]
        public void RoomJoinOutsideLobbyReturnsStatusFive()
        {
            RequestGateResult result = WorldRequestGatePolicy.Evaluate(0x38, 1, 7, 3);

            Assert.Equal(RequestGateAction.ReplyStatus, result.Action);
            Assert.Equal((ushort)5, result.Code);
        }

        [Fact]
        public void UncataloguedOpcodeIsNotPreempted()
        {
            Assert.True(WorldRequestGatePolicy.Evaluate(0x47, 0, 0, 0).Allowed);
        }
    }
}
