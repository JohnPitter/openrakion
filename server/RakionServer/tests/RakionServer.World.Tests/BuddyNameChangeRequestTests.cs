using System.Text;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BuddyNameChangeRequestTests
    {
        [Fact]
        public void ParsesNameAndIgnoresCipherPadding()
        {
            Assert.True(BuddyNameChangeRequest.TryParse(
                new byte[] { (byte)'B', (byte)'u', (byte)'d', 0, 0xAA }, out var request));
            Assert.Equal("Bud", request.Name);
        }

        [Fact]
        public void RejectsEmptyUnterminatedOrOversizedName()
        {
            Assert.False(BuddyNameChangeRequest.TryParse(new byte[] { 0 }, out _));
            Assert.False(BuddyNameChangeRequest.TryParse(Encoding.ASCII.GetBytes("Buddy"), out _));
            Assert.False(BuddyNameChangeRequest.TryParse(
                Encoding.ASCII.GetBytes("123456789012\0"), out _));
        }
    }
}
