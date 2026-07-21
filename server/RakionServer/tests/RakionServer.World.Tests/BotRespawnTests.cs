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
    }
}
