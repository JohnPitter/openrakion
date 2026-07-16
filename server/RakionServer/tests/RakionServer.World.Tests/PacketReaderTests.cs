using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class PacketReaderTests
    {
        [Fact]
        public void CString_DefaultLimitWorksAfterALeadingField()
        {
            var reader = new PacketReader(new byte[] { 1, (byte)'A', (byte)'F', (byte)'K', 0 });

            Assert.Equal(1, reader.Byte());
            Assert.Equal("AFK", reader.CString());
            Assert.Equal(0, reader.Remaining);
        }

        [Fact]
        public void TryCString_RequiresTerminatorWithinMaximumLength()
        {
            var valid = new PacketReader(new byte[] { (byte)'1', (byte)'2', 0, 9 });
            var tooLong = new PacketReader(new byte[] { (byte)'1', (byte)'2', (byte)'3', 0 });
            var unterminated = new PacketReader(new byte[] { (byte)'1', (byte)'2' });

            Assert.True(valid.TryCString(2, out string value));
            Assert.Equal("12", value);
            Assert.Equal(1, valid.Remaining);
            Assert.False(tooLong.TryCString(2, out _));
            Assert.False(unterminated.TryCString(2, out _));
        }
    }
}
