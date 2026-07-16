using System;
using RakionServer.World.Database;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class PresentFrameGoldenTests
    {
        [Fact]
        public void Peek_MatchesOriginalCallbackLayout() =>
            Assert.Equal(
                "6B0000010000001004000000000000000000000000000000",
                Convert.ToHexString(LobbyFrames.PresentPeekAck(
                    new PresentPeekResult(PresentPeekStatus.Success, 1, 1040))));

        [Fact]
        public void PeekEmpty_MatchesOriginalStatus() =>
            Assert.Equal(
                "6B0001000000000000000000000000000000000000000000",
                Convert.ToHexString(LobbyFrames.PresentPeekAck(
                    new PresentPeekResult(PresentPeekStatus.Empty))));

        [Theory]
        [InlineData(PresentAcceptStatus.Success, "6C0000000000000000000000")]
        [InlineData(PresentAcceptStatus.SlotOccupied, "6C0003000000000000000000")]
        [InlineData(PresentAcceptStatus.Failed, "6C0004000000000000000000")]
        public void Accept_MatchesOriginalCallbackLayout(PresentAcceptStatus status, string expected) =>
            Assert.Equal(expected, Convert.ToHexString(
                LobbyFrames.PresentAcceptAck(new PresentAcceptResult(status))));

        [Theory]
        [InlineData(PresentDisposeStatus.Success, "6D0000000000000000000000")]
        [InlineData(PresentDisposeStatus.Failed, "6D0004000000000000000000")]
        public void Dispose_MatchesOriginalCallbackLayout(PresentDisposeStatus status, string expected) =>
            Assert.Equal(expected, Convert.ToHexString(
                LobbyFrames.PresentDisposeAck(new PresentDisposeResult(status))));

        [Fact]
        public void Notification_ContainsItemsAndAccountName() =>
            Assert.Equal(
                "6A000210040000D804000074657374000000000000000000",
                Convert.ToHexString(LobbyFrames.PresentNotification([1040, 1240], "test")));
    }
}
