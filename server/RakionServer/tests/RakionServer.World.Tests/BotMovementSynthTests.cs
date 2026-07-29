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
        public void LocomotionAnimationPublishesOnlyOnTransition()
        {
            var bot = new BotPlayer();

            Assert.True(bot.ShouldPublishControls(BotControls.W));
            Assert.False(bot.ShouldPublishControls(BotControls.W));
            Assert.True(bot.ShouldPublishControls(BotControls.None));
            Assert.False(bot.ShouldPublishControls(BotControls.None));
        }

        [Fact]
        public void ArcherAttackAnimationPublishesCapturedMultiphaseProfile()
        {
            var bot = new BotPlayer { CharClass = 1 };
            bot.SetEngineIntent(BotControls.None, true);
            bot.ObserveEngineAttack(1000);

            Assert.True(bot.TryTakeAttackAnimation(1000, out byte first));
            Assert.Equal(25, first);
            Assert.False(bot.TryTakeAttackAnimation(1546, out _));
            Assert.True(bot.TryTakeAttackAnimation(1547, out byte second));
            Assert.Equal(24, second);
            Assert.True(bot.TryTakeAttackAnimation(1703, out byte third));
            Assert.Equal(12, third);
            Assert.False(bot.TryTakeAttackAnimation(2000, out _));
        }

        [Fact]
        public void UncapturedClassKeepsSinglePhaseFallback()
        {
            var bot = new BotPlayer { CharClass = 2 };
            bot.SetEngineIntent(BotControls.None, true);
            bot.ObserveEngineAttack(1000);

            Assert.True(bot.TryTakeAttackAnimation(1000, out byte animation));
            Assert.Equal(25, animation);
            Assert.False(bot.TryTakeAttackAnimation(2000, out _));
        }

        [Fact]
        public void ArcherAttackCyclesAllCapturedProfiles()
        {
            var bot = new BotPlayer { CharClass = 1 };
            StartAttack(bot, 1000);
            Assert.True(bot.TryTakeAttackAnimation(1000, out _));

            StartAttack(bot, 2000);
            Assert.True(bot.TryTakeAttackAnimation(2000, out byte first));
            Assert.Equal(27, first);
            Assert.True(bot.TryTakeAttackAnimation(2297, out byte second));
            Assert.Equal(26, second);
            Assert.True(bot.TryTakeAttackAnimation(2407, out byte third));
            Assert.Equal(18, third);

            StartAttack(bot, 3000);
            Assert.True(bot.TryTakeAttackAnimation(3000, out byte fourth));
            Assert.Equal(0, fourth);
            Assert.True(bot.TryTakeAttackAnimation(3554, out byte fifth));
            Assert.Equal(1, fifth);
        }

        [Fact]
        public void HitReactionCancelsPendingAttackPresentation()
        {
            var bot = new BotPlayer { CharClass = 1 };
            StartAttack(bot, 1000);
            Assert.True(bot.TryTakeAttackAnimation(1000, out _));

            bot.BeginHitReaction(1100);

            Assert.False(bot.TryTakeAttackAnimation(2000, out _));
        }

        [Fact]
        public void PauseClearsEngineAndPublishedActivity()
        {
            var bot = new BotPlayer();
            bot.SetEngineIntent(BotControls.W, true);
            bot.ShouldPublishControls(BotControls.W);
            bot.ObserveEngineAttack(1000);

            bot.PauseEngine();

            Assert.Equal(BotControls.None, bot.EngineControls);
            Assert.False(bot.EngineAttacking);
            Assert.False(bot.IsMoving);
            Assert.True(bot.ShouldPublishControls(BotControls.W));
            Assert.False(bot.TryTakeAttackAnimation(2000, out _));
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

        /// <summary>
        /// Golden da captura humano×humano (28/07/2026): a vítima publica `0x0311 kind=2` sobre si
        /// mesma alternando `(01,02)` e `(02,01)` nos golpes que não derrubam, `(0F,07)` no que
        /// derruba. Nesse perfil, o terminador é `01` nos 26 frames medidos e nunca o assento de
        /// quem golpeou; a captura posterior também encontrou `00`, ainda sem contexto fechado.
        /// </summary>
        [Theory]
        [InlineData(BotDamageReaction.StaggerA, 0x01, 0x02)]
        [InlineData(BotDamageReaction.StaggerB, 0x02, 0x01)]
        [InlineData(BotDamageReaction.Knockdown, 0x0f, 0x07)]
        public void SynthesizeDamage_MatchesCapturedVictimReaction(
            BotDamageReaction reaction, byte first, byte second)
        {
            byte[] packet = BotMovement.SynthesizeDamage(10, 99, reaction);

            Assert.True(GameplayActionDatagram.TryParseAnimation(packet, out var action));
            Assert.Equal(10, action.Header.SourceSlot);
            Assert.Equal(10, action.SourceEcho);
            Assert.Equal(99u, action.Header.Sequence);
            Assert.Equal(PlayerAnimationKind.Damage, action.Kind);
            Assert.True(action.HasExtendedPayload);
            Assert.Equal(first, packet[9]);
            Assert.Equal(second, packet[10]);
            Assert.Equal(0x01, packet[11]);
        }

        /// <summary>
        /// Golden byte a byte contra a captura humano×humano de 28/07/2026. Os hex são corpos de
        /// túnel `0x57` gravados do fio, dos dois jogadores reais apanhando — a síntese do domínio
        /// tem que reproduzi-los exatamente. A captura é o oráculo do teste, nunca a implementação.
        /// </summary>
        [Theory]
        [InlineData(1, BotDamageReaction.StaggerA, "1103" + "01" + "02" + "0102" + "01")]
        [InlineData(1, BotDamageReaction.StaggerB, "1103" + "01" + "02" + "0201" + "01")]
        [InlineData(1, BotDamageReaction.Knockdown, "1103" + "01" + "02" + "0F07" + "01")]
        [InlineData(0, BotDamageReaction.Knockdown, "1103" + "00" + "02" + "0F07" + "01")]
        public void SynthesizeDamage_ReproducesCapturedTunnelBody(
            byte seat, BotDamageReaction reaction, string expectedHex)
        {
            byte[] datagram = BotMovement.SynthesizeDamage(seat, 4242, reaction);

            byte[] body = GameplayActionDatagram.BuildTunnelPayload(datagram);

            Assert.Equal(expectedHex, Convert.ToHexString(body));
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

        [Theory]
        [InlineData(0x19)]
        [InlineData(0x18)]
        [InlineData(0x0c)]
        public void SynthesizeAttack_TransportsScheduledAnimation(
            byte expectedAnimation)
        {
            byte[] packet = BotMovement.SynthesizeAttack(
                10,
                4,
                expectedAnimation);

            Assert.Equal((byte)PlayerAnimationKind.Attack, packet[8]);
            Assert.Equal(expectedAnimation, packet[9]);
        }

        private static void StartAttack(BotPlayer bot, long nowMs)
        {
            bot.SetEngineIntent(BotControls.None, false);
            bot.ObserveEngineAttack(nowMs - 1);
            bot.SetEngineIntent(BotControls.None, true);
            bot.ObserveEngineAttack(nowMs);
        }
    }
}
