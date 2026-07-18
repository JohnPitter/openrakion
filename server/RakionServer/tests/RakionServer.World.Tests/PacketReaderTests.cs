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

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        public void Constructor_RejectsOffsetOutsidePacket(int offset)
        {
            Assert.Throws<EndOfPacketException>(() =>
                new PacketReader(new byte[2], offset));
        }

        [Fact]
        public void Skip_RejectsNegativeAndPastEndWithoutMovingReader()
        {
            var reader = new PacketReader(new byte[2]);

            Assert.Throws<EndOfPacketException>(() => reader.Skip(-1));
            Assert.Equal(0, reader.Position);
            Assert.Throws<EndOfPacketException>(() => reader.Skip(3));
            Assert.Equal(0, reader.Position);

            reader.Skip(2);
            Assert.Equal(0, reader.Remaining);
        }

        [Fact]
        public void CanRead_RejectsIntegerOverflow()
        {
            var reader = new PacketReader(new byte[1]);

            Assert.False(reader.CanRead(int.MaxValue));
            Assert.Throws<EndOfPacketException>(() => reader.Bytes(int.MaxValue));
        }
    }
}
