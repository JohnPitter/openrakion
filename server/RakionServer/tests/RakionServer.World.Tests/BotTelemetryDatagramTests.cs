using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BotTelemetryDatagramTests
    {
        [Fact]
        public void Wrap_RoundTripsGameplayWithoutChangingIt()
        {
            byte[] gameplay = System.Convert.FromHexString(
                "1103630000000A0A0109");

            byte[] packet = BotTelemetryDatagram.Wrap(gameplay);

            Assert.True(BotTelemetryDatagram.TryUnwrap(packet, out var decoded));
            Assert.Equal(gameplay, decoded.ToArray());
        }

        [Fact]
        public void Wrap_HeadlessRelay_PreservesGameplayAndMarksRouting()
        {
            byte[] gameplay = System.Convert.FromHexString(
                "1103630000000A0A0109");

            byte[] packet = BotTelemetryDatagram.Wrap(gameplay, headlessRelay: true);

            Assert.True(BotTelemetryDatagram.TryUnwrap(
                packet, out var decoded, out bool headlessRelay));
            Assert.True(headlessRelay);
            Assert.Equal(gameplay, decoded.ToArray());
        }

        [Theory]
        [InlineData("7AB00000")]
        [InlineData("7AB0020011")]
        [InlineData("1103630000000A0A0109")]
        public void TryUnwrap_RejectsInvalidEnvelope(string hex)
        {
            Assert.False(BotTelemetryDatagram.TryUnwrap(
                System.Convert.FromHexString(hex), out _));
        }
    }
}
