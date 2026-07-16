using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldChatFrameGoldenTests
    {
        [Fact]
        public void MessageContainsSenderSeatAndNullTerminatedText()
        {
            byte[] frame = FieldChatFrames.Message(10, "hello");

            Assert.Equal("0A68656C6C6F00", System.Convert.ToHexString(frame));
        }
    }
}
