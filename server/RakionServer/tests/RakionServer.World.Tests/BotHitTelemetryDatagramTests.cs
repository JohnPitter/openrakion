using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BotHitTelemetryDatagramTests
    {
        [Fact]
        public void Build_RoundTripsSequenceAndTargetSeat()
        {
            byte[] packet = BotHitTelemetryDatagram.Build(42, 10);

            Assert.True(BotHitTelemetryDatagram.TryParse(packet, out var hit));
            Assert.Equal(42u, hit.Sequence);
            Assert.Equal((byte)10, hit.TargetSeat);
        }

        [Theory]
        [InlineData("7BB000000000")]
        [InlineData("7AB0010000000A")]
        [InlineData("7BB0000000000A")]
        public void TryParse_RejectsMalformedPacket(string hex)
        {
            Assert.False(BotHitTelemetryDatagram.TryParse(
                System.Convert.FromHexString(hex), out _));
        }
    }
}
