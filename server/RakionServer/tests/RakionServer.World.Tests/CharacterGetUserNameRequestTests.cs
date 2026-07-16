using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterGetUserNameRequestTests
    {
        [Fact]
        public void TryParse_ReadsCStringAndIgnoresPadding()
        {
            Assert.True(CharacterGetUserNameRequest.TryParse(
                new byte[] { 0x74, 0x65, 0x73, 0x74, 0, 0, 0 }, out var request));
            Assert.Equal("test", request.Value);
        }

        [Fact]
        public void TryParse_RequiresTerminator() =>
            Assert.False(CharacterGetUserNameRequest.TryParse(new byte[] { 1, 2 }, out _));

        [Theory]
        [InlineData("123456789012", true)]
        [InlineData("1234567890123", false)]
        public void IsWithinOriginalLimit_UsesStrictThirteenByteBoundary(
            string value, bool expected) =>
            Assert.Equal(expected, new CharacterGetUserNameRequest(value).IsWithinOriginalLimit);
    }
}
