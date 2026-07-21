using System;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BotCombatTests
    {
        private static (Field Field, PlayerRec Attacker) Match()
        {
            var field = new Field(1)
            {
                Mode = (byte)GameMode.Deathmatch,
                State = 2,
                Phase = MatchPhase.Playing
            };
            PlayerRec attacker = field.Slots[0];
            attacker.State = 4;
            attacker.Position = BotVector.Zero;
            attacker.Heading = 0;
            return (field, attacker);
        }

        private static PlayerRec AddBot(Field field, byte team, BotVector position)
        {
            var bot = new BotPlayer { Name = "B", Team = team, Position = position };
            bot.InitHealth(1);
            int seat = field.AddBot(bot, team);
            field.Slots[seat].State = 4;
            field.Slots[seat].Position = position;
            return field.Slots[seat];
        }

        [Fact]
        public void MeleeAttack_DamagesOnlyNearestEnemyInFront()
        {
            var match = Match();
            PlayerRec nearest = AddBot(match.Field, 1, new BotVector(0, 0, 200));
            PlayerRec farther = AddBot(match.Field, 1, new BotVector(0, 0, 400));
            int fartherHealth = farther.Bot!.Health;

            bool applied = BotCombat.TryResolveMeleeAttack(
                match.Field, match.Attacker, 1000, 40, out BotCombat.BotHit hit);

            Assert.True(applied);
            Assert.Same(nearest, hit.BotRecord);
            Assert.Equal(fartherHealth, farther.Bot.Health);
        }

        [Fact]
        public void MeleeAttack_UsesAttackerHeading()
        {
            var match = Match();
            match.Attacker.Heading = MathF.PI / 2;
            PlayerRec target = AddBot(match.Field, 1, new BotVector(300, 0, 0));

            Assert.True(BotCombat.TryResolveMeleeAttack(
                match.Field, match.Attacker, 1000, 40, out BotCombat.BotHit hit));
            Assert.Same(target, hit.BotRecord);
        }

        [Theory]
        [InlineData(1, 0, -200)]
        [InlineData(1, 0, 10000)]
        [InlineData(0, 0, 200)]
        public void MeleeAttack_RejectsBehindOutOfRangeOrSameTeam(byte team, float x, float z)
        {
            var match = Match();
            AddBot(match.Field, team, new BotVector(x, 0, z));

            Assert.False(BotCombat.TryResolveMeleeAttack(
                match.Field, match.Attacker, 1000, 40, out _));
        }

        [Fact]
        public void MeleeAttack_SuppressesDuplicateHookEmission()
        {
            var match = Match();
            PlayerRec target = AddBot(match.Field, 1, new BotVector(0, 0, 100));

            Assert.True(BotCombat.TryResolveMeleeAttack(
                match.Field, match.Attacker, 1000, 20, out _));
            Assert.False(BotCombat.TryResolveMeleeAttack(
                match.Field, match.Attacker, 1249, 20, out _));
            Assert.True(BotCombat.TryResolveMeleeAttack(
                match.Field, match.Attacker, 1250, 20, out _));
            Assert.Equal(target.Bot!.MaxHealth - 40, target.Bot.Health);
        }

        [Fact]
        public void MeleeAttack_KillsBotWhenHealthDepletes()
        {
            var match = Match();
            PlayerRec target = AddBot(match.Field, 1, new BotVector(0, 0, 100));

            Assert.True(BotCombat.TryResolveMeleeAttack(
                match.Field, match.Attacker, 1000, 999, out BotCombat.BotHit hit));
            Assert.True(hit.Died);
            Assert.False(target.Bot!.Alive);
            Assert.Equal(0, target.Bot.Health);
            Assert.Equal(2u, target.Bot.LifecycleSequence);
        }
    }
}
