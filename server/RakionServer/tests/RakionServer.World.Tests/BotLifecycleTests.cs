using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Ciclo de vida do BOT no domínio (sem rede): alocação por time, contagem (bot ≠ humano),
    /// fallback de time cheio, reset de HP por round e remoção/limpeza. Garante as invariantes que o
    /// serviço (WorldServer.AddBotToField/DiscardBots) e o motor de IA dependem — tudo de raiz,
    /// sem ClientSession nem wire.
    /// </summary>
    public class BotLifecycleTests
    {
        private static Field NewField() => new Field(1) { Mode = 1, MaxRounds = 1, MinLevel = 1, MaxLevel = 10 };

        [Fact]
        public void AddBot_PlacesInRequestedTeamBlock()
        {
            var f = NewField();
            var t0 = f.AddBot("Rok", 5, 1, team: 0);
            var t1 = f.AddBot("Ares", 5, 1, team: 1);

            Assert.NotNull(t0);
            Assert.NotNull(t1);
            Assert.InRange(t0!.Value.Seat, 0, 9);     // time 0 = slots 0..9
            Assert.InRange(t1!.Value.Seat, 10, 0x13); // time 1 = slots 10..19
            Assert.Equal(0, t0.Value.Bot.Team);
            Assert.Equal(1, t1.Value.Bot.Team);
        }

        [Fact]
        public void Bot_IsOccupantButNotHuman()
        {
            var f = NewField();
            var added = f.AddBot("Karion", 5, 1, team: 1);

            var rec = f.RecAt(added!.Value.Seat)!;
            Assert.True(rec.IsBot);
            Assert.True(rec.Occupied);
            Assert.Equal(3, rec.State);          // ready (mesmo estado de um humano recém-alocado)
            Assert.Equal(1, f.BotCount);
            Assert.Equal(0, f.HumanCount);       // bots não entram em Players
            Assert.False(f.HasHuman);            // só bots -> nenhuma sala viva por bot
        }

        [Fact]
        public void AssignBotSeat_FallsBackToOtherTeam_WhenRequestedFull()
        {
            var f = NewField();
            for (int i = 0; i < 10; i++) Assert.NotNull(f.AddBot($"b{i}", 5, 1, team: 0)); // lota o time 0

            var overflow = f.AddBot("extra", 5, 1, team: 0);   // time 0 cheio -> cai no time 1
            Assert.NotNull(overflow);
            Assert.InRange(overflow!.Value.Seat, 10, 0x13);
            Assert.Equal(11, f.BotCount);
        }

        [Fact]
        public void StartRound_RestoresBotHp_AndPromotesToPlaying()
        {
            var f = NewField();
            var added = f.AddBot("Vyl", 5, 1, team: 1);
            var bot = added!.Value.Bot;
            bot.Hp = 1; bot.Dead = true; bot.SpawnedThisRound = true;

            f.StartRound();

            Assert.Equal(bot.MaxHp, bot.Hp);
            Assert.False(bot.Dead);
            Assert.False(bot.SpawnedThisRound);          // re-anuncia o spawn no novo round
            Assert.Equal(4, f.RecAt(added.Value.Seat)!.State); // ready -> playing
        }

        [Fact]
        public void RemoveAllBots_ClearsSeats_AndReturnsCount()
        {
            var f = NewField();
            f.AddBot("a", 5, 1, team: 0);
            f.AddBot("b", 5, 1, team: 1);

            int removed = f.RemoveAllBots();

            Assert.Equal(2, removed);
            Assert.Equal(0, f.BotCount);
            foreach (var r in f.Slots) Assert.False(r.IsBot);
        }

        [Fact]
        public void ClearBotSeat_EmptiesOnlyThatSeat()
        {
            var f = NewField();
            int a = f.AddBot("a", 5, 1, team: 0)!.Value.Seat;
            int b = f.AddBot("b", 5, 1, team: 1)!.Value.Seat;

            f.ClearBotSeat(a);

            Assert.False(f.RecAt(a)!.IsBot);
            Assert.True(f.RecAt(b)!.IsBot);
            Assert.Equal(1, f.BotCount);
        }

        [Fact]
        public void EphemeralBotId_IsUniquePerField()
        {
            var f = NewField();
            var a = f.AddBot("a", 5, 1, team: 0)!.Value.Bot;
            var b = f.AddBot("b", 5, 1, team: 1)!.Value.Bot;
            Assert.NotEqual(a.Id, b.Id);
        }
    }
}
