using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BotCombatTests
    {
        private static (Field Field, PlayerRec Attacker, byte BotSeat) Match(
            byte botTeam, BotVector attackerPosition, BotVector botPosition)
        {
            var field = new Field(1)
            {
                Mode = (byte)GameMode.Deathmatch,
                State = 2,
                Phase = MatchPhase.Playing
            };
            var attacker = field.Slots[0];
            attacker.State = 4;
            attacker.Position = attackerPosition;

            var bot = new BotPlayer { Name = "B", Team = botTeam, Position = botPosition };
            bot.InitHealth(1);
            int seat = field.AddBot(bot, botTeam);
            field.Slots[seat].State = 4;
            field.Slots[seat].Position = botPosition;
            return (field, attacker, (byte)seat);
        }

        [Fact]
        public void ConfirmedHit_DamagesOnlyDeclaredEnemyBotInRange()
        {
            var match = Match(1, new BotVector(120, 0, 120), new BotVector(100, 0, 100));
            int before = match.Field.Slots[match.BotSeat].Bot!.Health;

            bool applied = BotCombat.TryApplyConfirmedHit(
                match.Field, match.Attacker, match.BotSeat, 40, out var hit);

            Assert.True(applied);
            Assert.False(hit.Died);
            Assert.Equal(before - 40, hit.BotRecord.Bot!.Health);
        }

        [Fact]
        public void ConfirmedHit_KillsDeclaredBotWhenHealthDepletes()
        {
            var match = Match(1, default, default);

            bool applied = BotCombat.TryApplyConfirmedHit(
                match.Field, match.Attacker, match.BotSeat, 999, out var hit);

            Assert.True(applied);
            Assert.True(hit.Died);
            Assert.False(hit.BotRecord.Bot!.Alive);
            Assert.Equal(0, hit.BotRecord.Bot.Health);
            Assert.Equal(2u, hit.BotRecord.Bot.LifecycleSequence);
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(false, 10000)]
        public void ConfirmedHit_RejectsInvalidTeamOrRange(bool sameTeam, float botX)
        {
            var match = Match(sameTeam ? (byte)0 : (byte)1, default, new BotVector(botX, 0, 0));

            Assert.False(BotCombat.TryApplyConfirmedHit(
                match.Field, match.Attacker, match.BotSeat, 40, out _));
        }
    }
}
