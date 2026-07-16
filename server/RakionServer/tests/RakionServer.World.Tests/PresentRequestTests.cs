using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class PresentRequestTests
    {
        [Fact]
        public void Accept_ParsesLogicalPrefixAndIgnoresTail()
        {
            Assert.True(PresentAcceptRequest.TryParse(
                new byte[] { 0x78, 0x56, 0x34, 0x12, 17, 0, 0xAA }, out var request));
            Assert.Equal(0x12345678, request.PendingId);
            Assert.Equal((ushort)17, request.Slot);
        }

        [Fact]
        public void Dispose_ParsesLogicalPrefixAndIgnoresTail()
        {
            Assert.True(PresentDisposeRequest.TryParse(
                new byte[] { 0x78, 0x56, 0x34, 0x12, 0xAA }, out var request));
            Assert.Equal(0x12345678, request.PendingId);
        }

        [Fact]
        public void ParsersRejectTruncatedPayloads()
        {
            Assert.False(PresentAcceptRequest.TryParse(new byte[5], out _));
            Assert.False(PresentDisposeRequest.TryParse(new byte[3], out _));
        }
    }
}
