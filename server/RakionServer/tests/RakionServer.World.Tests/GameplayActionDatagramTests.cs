using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class GameplayActionDatagramTests
    {
        [Fact]
        public void Move_ParsesCapturedEngineLayout()
        {
            byte[] packet = System.Convert.FromHexString(
                "0A032700000000650020005E01000092090000A5000000000000");

            Assert.True(GameplayActionDatagram.TryParseMove(packet, out var action));
            Assert.Equal(GameplayActionDatagram.MoveType, action.Header.Type);
            Assert.Equal(0x27u, action.Header.Sequence);
            Assert.Equal((byte)0, action.Header.SourceSlot);
            Assert.Equal((ushort)101, action.DeltaMilliseconds);
            Assert.Equal((byte)0, action.SourceEcho);
            Assert.Equal(PlayerActionState.Attack, action.State);
            Assert.Equal((byte)0, action.ActionCode);
            Assert.Equal((short)350, action.PositionX);
            Assert.Equal((short)0, action.PositionY);
            Assert.Equal((short)2450, action.PositionZ);
            Assert.Equal((byte)0xa5, action.AngleByte);
            Assert.Equal((short)0, action.ViewRotationX);
            Assert.Equal((short)0, action.ViewRotationY);
            Assert.Equal((short)0, action.ViewRotationZ);
        }

        [Fact]
        public void Move_ParsesViewRotationAtFinalOffsets()
        {
            byte[] packet = System.Convert.FromHexString(
                "0A032700000000650020005E01000092090000A50100FEFF3412");

            Assert.True(GameplayActionDatagram.TryParseMove(packet, out var action));
            Assert.Equal((short)1, action.ViewRotationX);
            Assert.Equal((short)-2, action.ViewRotationY);
            Assert.Equal((short)0x1234, action.ViewRotationZ);
        }

        [Theory]
        [InlineData("0F03280000000A0A080001000003", 0x030f)]
        [InlineData("1103630000000A0A0100", 0x0311)]
        [InlineData("1103630000000A0A01000000", 0x0311)]
        public void CompanionStreams_AcceptExactCapturedShapes(string hex, ushort type)
        {
            byte[] packet = System.Convert.FromHexString(hex);

            Assert.True(GameplayActionDatagram.TryParseHeader(packet, out var header));
            Assert.Equal(type, header.Type);
        }

        [Theory]
        [InlineData("0A030000000000")]
        [InlineData("0F030000000000000000000000")]
        [InlineData("110300000000000000")]
        [InlineData("1903000000000000")]
        public void InvalidOrUnsupportedShape_IsRejected(string hex)
        {
            Assert.False(GameplayActionDatagram.TryParseHeader(
                System.Convert.FromHexString(hex), out _));
        }
    }
}
