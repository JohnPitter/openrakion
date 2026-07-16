using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterDeleteRequestTests
    {
        [Fact]
        public void ParsesIdAndKeyWhileIgnoringCipherPadding()
        {
            Assert.True(CharacterDeleteRequest.TryParse(
                new byte[] { 0x78, 0x56, 0x34, 0x12, (byte)'A', (byte)'b', 0, 0xAA },
                out var request));
            Assert.Equal(0x12345678, request.CharacterId);
            Assert.Equal("Ab", request.DeleteKey);
        }

        [Fact]
        public void MatchesOriginalTenByteTruncation()
        {
            Assert.True(CharacterDeleteRequest.TryParse(
                new byte[] { 1, 0, 0, 0, (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5',
                    (byte)'6', (byte)'7', (byte)'8', (byte)'9', (byte)'0', (byte)'X', 0 },
                out var request));
            Assert.Equal("1234567890", request.DeleteKey);
        }

        [Fact]
        public void RejectsTruncatedBody()
        {
            Assert.False(CharacterDeleteRequest.TryParse(Array.Empty<byte>(), out _));
            Assert.False(CharacterDeleteRequest.TryParse(new byte[] { 1, 0, 0, 0 }, out _));
        }
    }
}
