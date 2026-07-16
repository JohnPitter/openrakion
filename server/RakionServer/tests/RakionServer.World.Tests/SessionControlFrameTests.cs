using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class SessionControlFrameTests
    {
        [Fact]
        public void DisconnectCarriesConnectionLogReasonAndGameInfoIds()
        {
            Assert.Equal(
                "44332211E70088776655",
                System.Convert.ToHexString(SessionControlFrames.Disconnect(
                    0x11223344, 0x00e7, 0x55667788)));
        }
    }
}
