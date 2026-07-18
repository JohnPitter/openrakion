using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>Sintetizador do 0x030A do bot: formato de 26 bytes, assento na origem e roundtrip da posição.</summary>
    public sealed class BotMovementSynthTests
    {
        [Fact]
        public void SynthesizeMove_HasCorrectHeaderSeatAndSize()
        {
            byte[] p = BotMovement.SynthesizeMove(seat: 12, new BotVector(100, 0, 250), heading: 0f, sequence: 7);

            Assert.Equal(26, p.Length);
            Assert.Equal(0x0a, p[0]);
            Assert.Equal(0x03, p[1]);
            Assert.Equal(12, p[6]);   // assento de origem do bot
            Assert.Equal(12, p[9] & 0x1f);   // eco exigido pelo parser do peer
        }

        [Fact]
        public void SynthesizeMove_PositionRoundtripsThroughReader()
        {
            var pos = new BotVector(1234, -50, -600);
            byte[] p = BotMovement.SynthesizeMove(10, pos, 0f, 1);

            Assert.True(BotMovement.TryReadPosition(p, out BotVector read));
            Assert.Equal(1234f, read.X);
            Assert.Equal(-50f, read.Y);
            Assert.Equal(-600f, read.Z);
        }

        [Fact]
        public void TryReadPosition_RejectsNonMoveDatagram()
        {
            byte[] notMove = new byte[26];
            notMove[0] = 0x0f; notMove[1] = 0x03; // 0x030F sync, não move
            Assert.False(BotMovement.TryReadPosition(notMove, out _));
        }

        [Fact]
        public void SynthesizeDamage_ProducesExtendedBotReaction()
        {
            byte[] packet = BotMovement.SynthesizeDamage(10, 99);

            Assert.True(GameplayActionDatagram.TryParseAnimation(packet, out var action));
            Assert.Equal(10, action.Header.SourceSlot);
            Assert.Equal(10, action.SourceEcho);
            Assert.Equal(99u, action.Header.Sequence);
            Assert.Equal(PlayerAnimationKind.Damage, action.Kind);
            Assert.True(action.HasExtendedPayload);
        }
    }
}
