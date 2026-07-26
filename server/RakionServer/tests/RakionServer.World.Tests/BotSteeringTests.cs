using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>Testes do motor de IA puro do bot (perseguição, frenagem no melee, antecipação).</summary>
    public sealed class BotSteeringTests
    {
        private static readonly BotProfile Normal = BotProfile.Normal;

        [Fact]
        public void Step_OutOfMelee_MovesTowardTarget()
        {
            var pos = new BotVector(0, 0, 0);
            var target = new BotVector(0, 0, 1000);

            BotStep step = BotSteering.Step(pos, BotVector.Zero, Normal, target, BotVector.Zero, 0.1f);

            Assert.False(step.InMelee);
            Assert.True(step.Position.Z > 0, "deve avançar em direção ao alvo");
            Assert.True(step.Position.HorizontalDistanceTo(target) < pos.HorizontalDistanceTo(target),
                "distância ao alvo deve diminuir");
        }

        [Fact]
        public void Step_WithinMeleeRange_BrakesInsteadOfOrbiting()
        {
            var target = new BotVector(0, 0, 0);
            var pos = new BotVector(150f, 0, 0); // dentro do MeleeRange, fora do spacing mínimo

            var velocity = new BotVector(0, 0, 100);
            BotStep step = BotSteering.Step(pos, velocity, Normal, target, BotVector.Zero, 0.1f);

            Assert.True(step.InMelee);
            Assert.True(step.Velocity.Length < velocity.Length);
            Assert.Equal(0f, step.Velocity.X, 0.0001f);
        }

        [Fact]
        public void Step_AccelerationSmoothsVelocity_NoTeleport()
        {
            var pos = new BotVector(0, 0, 0);
            var target = new BotVector(0, 0, 1000);

            BotStep step = BotSteering.Step(pos, BotVector.Zero, Normal, target, BotVector.Zero, 0.1f);

            // Com aceleração 0.4 e velocidade inicial 0, o passo NÃO atinge a velocidade máxima de uma vez.
            float maxPerTick = Normal.MoveSpeed * 0.1f;
            Assert.True(step.Position.Length < maxPerTick, "aceleração deve suavizar (sem teleporte)");
        }

        [Fact]
        public void SmoothVelocity_ConvergesTowardSample()
        {
            var ema = BotVector.Zero;
            var sample = new BotVector(10, 0, 0);
            for (int i = 0; i < 20; i++)
                ema = BotSteering.SmoothVelocity(ema, sample, 0.4f);
            Assert.True(ema.X > 9f, "EMA deve convergir para a amostra");
        }

        [Fact]
        public void Anticipation_LeadsAMovingTarget()
        {
            var pos = new BotVector(0, 0, 0);
            var target = new BotVector(0, 0, 1000);
            var targetVel = new BotVector(500, 0, 0); // alvo se movendo em X na escala wire

            BotStep hard = BotSteering.Step(pos, BotVector.Zero, BotProfile.For(BotDifficulty.Hard),
                target, targetVel, 0.1f);
            BotStep easy = BotSteering.Step(pos, BotVector.Zero, BotProfile.For(BotDifficulty.Easy),
                target, targetVel, 0.1f);

            // Hard antecipa (Anticipation 0.5) → mira mais à frente em X que Easy (Anticipation 0).
            Assert.True(hard.Position.X > easy.Position.X, "dificuldade Hard antecipa o alvo");
        }

        [Fact]
        public void BotPlayer_Tick_AdvancesAndTracksTarget()
        {
            var bot = new BotPlayer { Name = "Rok", Profile = BotProfile.Normal, Position = new BotVector(0, 0, 0) };
            var target = new BotVector(0, 0, 2000);

            for (int i = 0; i < 10; i++) bot.Tick(target, 0.1f);

            Assert.True(bot.Position.Z > 0, "bot deve ter avançado em direção ao alvo");
            Assert.True(bot.Position.HorizontalDistanceTo(target) < 2000f);
        }

        [Fact]
        public void BotPlayer_Tick_ReachesCapturedMapScaleWithinTenSeconds()
        {
            var bot = new BotPlayer
            {
                Name = "Rok",
                Profile = BotProfile.Normal,
                Position = BotVector.Zero
            };
            var target = new BotVector(0, 0, 3000);

            for (int i = 0; i < 100; i++) bot.Tick(target, 0.1f);

            Assert.True(bot.Position.HorizontalDistanceTo(target) <= bot.Profile.MeleeRange,
                "o bot precisa alcançar a hitbox em escala wire durante uma rodada real");
        }

        [Fact]
        public void BotPlayer_Tick_DeadBotDoesNotMove()
        {
            var bot = new BotPlayer { Position = new BotVector(1, 2, 3), Alive = false };
            bot.Tick(new BotVector(0, 0, 50), 0.1f);
            Assert.Equal(new BotVector(1, 2, 3), bot.Position);
        }
    }
}
