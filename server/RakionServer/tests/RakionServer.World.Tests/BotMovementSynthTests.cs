using System;
using System.Buffers.Binary;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>Sintetizador do 0x030A do bot: formato de 26 bytes, assento na origem e roundtrip da posição.</summary>
    public sealed class BotMovementSynthTests
    {
        [Theory]
        [InlineData(BotControls.W, PlayerNormalAnimation.MoveForward)]
        [InlineData(BotControls.S, PlayerNormalAnimation.MoveBackward)]
        [InlineData(BotControls.A, PlayerNormalAnimation.MoveLeft)]
        [InlineData(BotControls.D, PlayerNormalAnimation.MoveRight)]
        [InlineData(BotControls.W | BotControls.A, PlayerNormalAnimation.MoveForwardLeft)]
        [InlineData(BotControls.W | BotControls.D, PlayerNormalAnimation.MoveForwardRight)]
        [InlineData(BotControls.S | BotControls.A, PlayerNormalAnimation.MoveBackwardLeft)]
        [InlineData(BotControls.S | BotControls.D, PlayerNormalAnimation.MoveBackwardRight)]
        [InlineData(BotControls.Space, PlayerNormalAnimation.Jump)]
        public void AnimationForControls_MapsCapturedHumanControls(
            BotControls controls,
            PlayerNormalAnimation expected)
        {
            Assert.Equal(expected, BotMovement.AnimationForControls(controls));
        }

        [Fact]
        public void SynthesizeMove_HasCorrectHeaderSeatAndSize()
        {
            byte[] p = BotMovement.SynthesizeMove(seat: 12, new BotVector(100, 0, 250), heading: 0f, sequence: 7);

            Assert.Equal(26, p.Length);
            Assert.Equal(0x0a, p[0]);
            Assert.Equal(0x03, p[1]);
            Assert.Equal(12, p[6]);   // assento de origem do bot
            Assert.Equal(12, p[9] & 0x1f);   // eco exigido pelo parser do peer
            Assert.Equal(0, p[10]);          // produtor v258 sempre serializa None
        }

        [Fact]
        public void SynthesizeMove_EncodesWalkingActionAndCompanionKeystate()
        {
            byte[] movement = BotMovement.SynthesizeMove(
                10, new BotVector(100, 0, 250), 0f, 7);
            byte[] keystate = BotMovement.SynthesizeKeystate(10, 8, moving: true);

            Assert.Equal((byte)PlayerActionState.Normal, movement[9] >> 5);
            Assert.Equal(0, movement[10]);
            Assert.True(GameplayActionDatagram.TryParseSync(keystate, out var sync));
            Assert.Equal(10, sync.Header.SourceSlot);
            Assert.Equal(10, sync.SourceEcho);
            Assert.Equal(0x08, sync.LifeState);
            Assert.Equal(0, sync.ControlMode);
            Assert.Equal(1, sync.ControlDetail);
        }

        [Fact]
        public void SynthesizeKeystate_EncodesIdleAfterMovementStops()
        {
            byte[] keystate = BotMovement.SynthesizeKeystate(10, 9, moving: false);

            Assert.True(GameplayActionDatagram.TryParseSync(keystate, out var sync));
            Assert.Equal(0, sync.ControlMode);
            Assert.Equal(3, sync.ControlDetail);
        }

        [Fact]
        public void LocomotionAnimation_RefreshesWithoutCuttingEveryTick()
        {
            var bot = new BotPlayer();

            Assert.True(bot.ShouldPublishControls(BotControls.W, 1_000));
            Assert.False(bot.ShouldPublishControls(BotControls.W, 1_150));
            Assert.True(bot.ShouldPublishControls(BotControls.W, 1_800));
            Assert.True(bot.ShouldPublishControls(BotControls.None, 1_950));
            Assert.False(bot.ShouldPublishControls(BotControls.None, 2_100));
        }

        [Fact]
        public void SynthesizeMove_PositionRoundtripsThroughReader()
        {
            var pos = new BotVector(123.45f, -0.5f, -60f);
            byte[] p = BotMovement.SynthesizeMove(10, pos, 0f, 1);

            Assert.True(BotMovement.TryReadPosition(p, out BotVector read));
            Assert.Equal(pos, read);
        }

        [Fact]
        public void SynthesizeMove_PoseRoundtripsHeading()
        {
            byte[] packet = BotMovement.SynthesizeMove(
                10, new BotVector(200, 5, -300), MathF.PI / 2, 2);

            Assert.True(BotMovement.TryReadPose(packet, out BotVector position, out float heading));
            Assert.Equal(new BotVector(200, 5, -300), position);
            Assert.InRange(heading, MathF.PI / 2 - 0.001f, MathF.PI / 2 + 0.001f);
        }

        [Fact]
        public void SynthesizeMove_KeepsAbsoluteHeadingOutOfAccumulatedViewDeltas()
        {
            byte[] packet = BotMovement.SynthesizeMove(
                10, new BotVector(100, 0, 250), MathF.PI / 2, 8);

            Assert.NotEqual(0, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(17)));
            Assert.Equal(-90, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(17)));
            Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(20)));
            Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(22)));
            Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(24)));
        }

        [Fact]
        public void TryReadPose_ConvertsCapturedWireHeadingToVisualFacing()
        {
            byte[] packet = BotMovement.SynthesizeMove(
                10, BotVector.Zero, 0f, sequence: 9);

            Assert.Equal(180, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(17)));
            Assert.True(BotMovement.TryReadPose(packet, out _, out float heading));
            Assert.InRange(heading, -0.001f, 0.001f);
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
            byte[] packet = BotMovement.SynthesizeDamage(10, 99, 1);

            Assert.True(GameplayActionDatagram.TryParseAnimation(packet, out var action));
            Assert.Equal(10, action.Header.SourceSlot);
            Assert.Equal(10, action.SourceEcho);
            Assert.Equal(99u, action.Header.Sequence);
            Assert.Equal(PlayerAnimationKind.Damage, action.Kind);
            Assert.True(action.HasExtendedPayload);
            Assert.Equal(0x0f, packet[9]);
            Assert.Equal(0x07, packet[10]);
            Assert.Equal(1, packet[11]);
        }

        [Theory]
        [InlineData(PlayerNormalAnimation.Stand, 1)]
        [InlineData(PlayerNormalAnimation.MoveForward, 4)]
        [InlineData(PlayerNormalAnimation.Jump, 12)]
        [InlineData(PlayerNormalAnimation.Rise, 14)]
        public void SynthesizeNormalAnimation_UsesExecNormalAnimIds(
            PlayerNormalAnimation animation, byte expected)
        {
            byte[] packet = BotMovement.SynthesizeNormalAnimation(10, 100, animation);

            Assert.Equal((byte)PlayerAnimationKind.Normal, packet[8]);
            Assert.Equal(expected, packet[9]);
        }

        // IDs medidos no fio de uma partida real: o cliente original golpeia com 0x19, 0x18 e
        // 0x0C. Os valores anteriores (0x1b/0x1a/0x12) existem no vocabulário mas eram os raros,
        // e o bot golpeava sem desenhar nada na tela do outro jogador.
        [Theory]
        [InlineData(BotAttackVariant.VariantA, 0x19)]
        [InlineData(BotAttackVariant.VariantB, 0x18)]
        [InlineData(BotAttackVariant.VariantC, 0x0c)]
        public void SynthesizeAttack_UsesCapturedHumanAnimations(
            BotAttackVariant variant, byte expectedAnimation)
        {
            byte[] packet = BotMovement.SynthesizeAttack(10, 4, variant);

            Assert.Equal((byte)PlayerAnimationKind.Attack, packet[8]);
            Assert.Equal(expectedAnimation, packet[9]);
        }
    }
}
