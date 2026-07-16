using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterSelectRequestTests
    {
        [Fact]
        public void ParsesLogicalPayloadWithoutPadding()
        {
            Assert.True(CharacterSelectRequest.TryParse(
                new byte[] { 0x78, 0x56, 0x34, 0x12 }, out CharacterSelectRequest request));
            Assert.Equal(0x12345678, request.CharacterId);
        }

        [Fact]
        public void IgnoresCipherPaddingAfterCharacterId()
        {
            Assert.True(CharacterSelectRequest.TryParse(
                new byte[] { 1, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD },
                out CharacterSelectRequest request));
            Assert.Equal(1, request.CharacterId);
        }

        [Fact]
        public void RejectsTruncatedPayload()
        {
            Assert.False(CharacterSelectRequest.TryParse(Array.Empty<byte>(), out _));
            Assert.False(CharacterSelectRequest.TryParse(new byte[] { 1, 2, 3 }, out _));
        }
    }
}
