using System;
using System.Text;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterCreateRequestTests
    {
        [Fact]
        public void ParsesLogicalBodyAndIgnoresCipherPadding()
        {
            Assert.True(CharacterCreateRequest.TryParse(
                Bytes("Probe\0", 2, 4, 0xAA), out var request));
            Assert.Equal("Probe", request.Name);
            Assert.Equal((byte)2, request.Class);
            Assert.Equal((byte)4, request.Slot);
        }

        [Fact]
        public void RejectsMissingTerminatorOrFields()
        {
            Assert.False(CharacterCreateRequest.TryParse(Encoding.ASCII.GetBytes("Probe"), out _));
            Assert.False(CharacterCreateRequest.TryParse(Bytes("Probe\0", 2), out _));
        }

        [Fact]
        public void PreservesOriginalTwelveByteWireBoundary()
        {
            Assert.True(CharacterCreateRequest.TryParse(Bytes("123456789012\0", 0, 0), out _));
            Assert.False(CharacterCreateRequest.TryParse(Bytes("1234567890123\0", 0, 0), out _));
        }

        private static byte[] Bytes(string text, params byte[] suffix)
        {
            byte[] prefix = Encoding.ASCII.GetBytes(text);
            byte[] result = new byte[prefix.Length + suffix.Length];
            prefix.CopyTo(result, 0);
            suffix.CopyTo(result, prefix.Length);
            return result;
        }
    }
}
