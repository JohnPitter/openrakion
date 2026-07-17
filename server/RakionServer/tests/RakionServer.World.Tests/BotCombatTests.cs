using System.Collections.Generic;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>Combate server-side do bot: dano de melee, morte, filtro de time e alcance.</summary>
    public sealed class BotCombatTests
    {
        private static Field GolemFieldWithBot(byte team, BotVector botPos)
        {
            var field = new Field(1) { Mode = (byte)GameMode.Golem, State = 2, Phase = MatchPhase.Playing };
            var bot = new BotPlayer { Name = "B", Team = team, Position = botPos };
            bot.InitHealth(1);   // MaxHealth = 110
            int seat = field.AddBot(bot, team);
            field.Slots[seat].State = 4;
            field.Slots[seat].Position = botPos;
            return field;
        }

        [Fact]
        public void MeleeAttack_DamagesEnemyBotInRange()
        {
            var field = GolemFieldWithBot(team: 1, new BotVector(100, 0, 100));
            int before = field.Slots[10].Bot!.Health;

            var hits = BotCombat.ResolveMeleeAttack(field, new BotVector(120, 0, 120), attackerTeam: 0, damage: 40);

            Assert.Single(hits);
            Assert.False(hits[0].Died);
            Assert.Equal(before - 40, hits[0].Bot.Bot!.Health);
        }

        [Fact]
        public void MeleeAttack_KillsBotWhenHealthDepleted()
        {
            var field = GolemFieldWithBot(team: 1, new BotVector(0, 0, 0));

            var hits = BotCombat.ResolveMeleeAttack(field, new BotVector(0, 0, 0), attackerTeam: 0, damage: 999);

            Assert.Single(hits);
            Assert.True(hits[0].Died);
            Assert.False(hits[0].Bot.Bot!.Alive);
            Assert.Equal(0, hits[0].Bot.Bot!.Health);
        }

        [Fact]
        public void MeleeAttack_IgnoresSameTeamBot()
        {
            var field = GolemFieldWithBot(team: 0, new BotVector(0, 0, 0));

            var hits = BotCombat.ResolveMeleeAttack(field, new BotVector(0, 0, 0), attackerTeam: 0, damage: 40);

            Assert.Empty(hits);   // bot é do mesmo time do atacante
        }

        [Fact]
        public void MeleeAttack_IgnoresBotOutOfRange()
        {
            var field = GolemFieldWithBot(team: 1, new BotVector(10000, 0, 10000));

            var hits = BotCombat.ResolveMeleeAttack(field, new BotVector(0, 0, 0), attackerTeam: 0, damage: 40);

            Assert.Empty(hits);   // fora do alcance de melee
        }
    }
}
