using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BotRespawnTests
    {
        [Theory]
        [InlineData((byte)GameMode.Deathmatch, BotRespawnPolicy.CompetitiveDelayMs)]
        [InlineData((byte)GameMode.TeamDeath, BotRespawnPolicy.CompetitiveDelayMs)]
        [InlineData((byte)GameMode.Boss, BotRespawnPolicy.CompetitiveDelayMs)]
        [InlineData((byte)GameMode.Golem, 0)]
        public void Policy_MatchesOriginalCompetitiveModes(byte mode, int expectedDelay)
        {
            Assert.Equal(expectedDelay, BotRespawnPolicy.DelayMs(mode));
        }

        [Fact]
        public void DeadBot_RespawnsOnlyAfterDeadlineWithFullHealth()
        {
            var bot = new BotPlayer { Name = "B" };
            bot.InitHealth(10);
            Assert.True(bot.TakeDamage(bot.MaxHealth));
            bot.ScheduleRespawn(1000, BotRespawnPolicy.CompetitiveDelayMs);

            Assert.False(bot.TryRespawn(7999));
            Assert.True(bot.TryRespawn(8000));
            Assert.True(bot.Alive);
            Assert.Equal(bot.MaxHealth, bot.Health);
            Assert.Equal(3u, bot.LifecycleSequence);
        }

        [Fact]
        public void AttackPattern_CyclesVariantsAndHitReactionStopsBot()
        {
            var bot = new BotPlayer
            {
                Velocity = new BotVector(10, 0, 20),
                TargetSeat = 3,
                NextAttackReadyMs = 1200
            };

            Assert.Equal(BotAttackVariant.VariantA, bot.NextAttackVariant());
            Assert.Equal(BotAttackVariant.VariantB, bot.NextAttackVariant());
            Assert.Equal(BotAttackVariant.VariantC, bot.NextAttackVariant());
            Assert.Equal(BotAttackVariant.VariantA, bot.NextAttackVariant());
            bot.BeginHitReaction(1000);
            Assert.Equal(1000 + BotPlayer.DamageReactionMs, bot.HitReactionUntilMs);
            Assert.Equal(BotVector.Zero, bot.Velocity);
            Assert.Equal(Field.NoSeat, bot.TargetSeat);
            Assert.Equal(bot.HitReactionUntilMs, bot.NextAttackReadyMs);
        }
    }
}
