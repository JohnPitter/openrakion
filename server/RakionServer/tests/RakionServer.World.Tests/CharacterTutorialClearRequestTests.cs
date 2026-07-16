using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterTutorialClearRequestTests
    {
        [Fact]
        public void AcceptsEmptyLogicalBodyAndZeroCipherPadding()
        {
            Assert.True(CharacterTutorialClearRequest.TryParse(System.Array.Empty<byte>()));
            Assert.True(CharacterTutorialClearRequest.TryParse(new byte[8]));
        }

        [Fact]
        public void RejectsUnexpectedLogicalData() =>
            Assert.False(CharacterTutorialClearRequest.TryParse(new byte[] { 0, 1 }));
    }
}
