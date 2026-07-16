using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class TunnelingRelayPolicyTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public void RelayOneRequiresAtLeastOneTunnelingEndpoint(
            bool senderTunneling, bool targetTunneling, bool expected)
        {
            var field = ActiveField();
            PlayerRec sender = Playing(0, senderTunneling);
            PlayerRec target = Playing(1, targetTunneling);

            Assert.Equal(expected,
                TunnelingRelayPolicy.ShouldRelayOne(field, sender, target));
        }

        [Fact]
        public void RelayAllDoesNotEchoToSender()
        {
            var field = ActiveField();
            PlayerRec sender = Playing(0, true);

            Assert.False(TunnelingRelayPolicy.ShouldRelayAll(field, sender, sender));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(2, false)]
        public void RelayRequiresActiveMatchAndAggregate(byte state, bool aggregate)
        {
            var field = new Field(1) { State = state, HasTunnelingClient = aggregate };
            PlayerRec sender = Playing(0, true);
            PlayerRec target = Playing(1, false);

            Assert.False(TunnelingRelayPolicy.ShouldRelayOne(field, sender, target));
        }

        private static Field ActiveField() => new(1)
        {
            State = 2,
            HasTunnelingClient = true
        };

        private static PlayerRec Playing(int slot, bool usesTunneling) => new()
        {
            Slot = slot,
            State = 4,
            UsesTunneling = usesTunneling
        };
    }
}
