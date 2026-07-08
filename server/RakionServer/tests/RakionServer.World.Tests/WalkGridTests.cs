using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>Colisão de parede do bot no modelo input-de-cliente: grid de chão pisável + deslize WASD.</summary>
    public class WalkGridTests
    {
        /// <summary>Semeia um corredor horizontal (Z≈0) grande o bastante p/ ATIVAR o grid.</summary>
        private static WalkGrid SeededCorridor()
        {
            var g = new WalkGrid();
            g.SeedPath(new Vec3(-40f, 0f, 0f), new Vec3(40f, 0f, 0f), halfWidth: 2.0f);
            return g;
        }

        [Fact]
        public void GridInativo_TudoAberto()
        {
            var g = new WalkGrid();
            g.MarkWalked(0f, 0f);   // poucas células -> abaixo do threshold
            Assert.True(g.IsOpen(999f, 999f));
        }

        [Fact]
        public void CorredorSemeado_DentroAberto_ForaBloqueado()
        {
            var g = SeededCorridor();
            Assert.True(g.IsOpen(0f, 0f));       // meio do corredor
            Assert.True(g.IsOpen(-39f, 1.5f));   // borda interna
            Assert.False(g.IsOpen(0f, 10f));     // "parede" (nunca pisada)
            Assert.False(g.IsOpen(0f, -10f));
        }

        [Fact]
        public void HumanoPisou_ViraChao()
        {
            var g = SeededCorridor();
            Assert.False(g.IsOpen(0f, 12f));
            g.MarkWalked(0f, 12f);               // humano andou ali -> chão aprendido
            Assert.True(g.IsOpen(0f, 12f));
        }

        [Fact]
        public void Integrate_DeslizaNaParede_EmVezDeAtravessar()
        {
            var g = SeededCorridor();
            var bot = new BotPlayer(1, "T", 1, 1, 0, BotDifficulty.Hard) { X = 0f, Z = 1.5f, Grid = g };
            // alvo diagonal p/ FORA do corredor (Z cresce além da parede): o passo cheio bloqueia,
            // o deslize mantém Z e avança X — nunca entra em célula fechada.
            for (int i = 0; i < 60; i++)
                bot.MoveToward(20f, 30f, standoff: 0f);
            Assert.True(g.IsOpen(bot.X, bot.Z), $"bot terminou em célula de parede ({bot.X:F1},{bot.Z:F1})");
            Assert.True(bot.X > 3f, $"bot não deslizou ao longo da parede (X={bot.X:F1})");
            Assert.True(bot.Z < 4f, $"bot atravessou a parede (Z={bot.Z:F1})");
        }

        [Fact]
        public void Knockback_NaoEmpurraParaDentroDaParede()
        {
            var g = SeededCorridor();
            var bot = new BotPlayer(1, "T", 1, 1, 0) { X = 0f, Z = 1.5f, Grid = g };
            bot.ApplyKnockback(fromX: 0f, fromZ: -5f, dist: 6f);   // empurrão p/ +Z (parede)
            Assert.True(g.IsOpen(bot.X, bot.Z), $"knockback terminou na parede ({bot.X:F1},{bot.Z:F1})");
        }
    }
}
