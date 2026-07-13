using System;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Níveis de inteligência (BotProfile/BotDifficulty) + movimento HUMANO do bot (velocidade com
    /// aceleração e orbitação no melee). Garante que a dificuldade escolhe parâmetros distintos, que
    /// o deslocamento ACELERA (não salta à velocidade plena) e que a orbitação circula o alvo mantendo
    /// distância — tudo no domínio, sem rede.
    /// </summary>
    public class BotProfileTests
    {
        [Fact]
        public void For_DistinctProfiles_HarderIsFasterTougherAndMoreAggressive()
        {
            var easy = BotProfile.For(BotDifficulty.Easy);
            var normal = BotProfile.For(BotDifficulty.Normal);
            var hard = BotProfile.For(BotDifficulty.Hard);

            Assert.True(hard.MoveSpeed > normal.MoveSpeed && normal.MoveSpeed > easy.MoveSpeed);
            Assert.True(hard.MaxHp > normal.MaxHp && normal.MaxHp > easy.MaxHp);
            Assert.True(hard.EngageRange > easy.EngageRange);              // Hard caça de mais longe
            Assert.True(hard.DecisionIntervalMs < easy.DecisionIntervalMs); // Hard reage mais rápido
            Assert.True(hard.ReactionDelayMs < easy.ReactionDelayMs);
            Assert.True(hard.LeadFactor > easy.LeadFactor);               // só Hard antecipa forte
            Assert.True(hard.ComboPool.Length >= easy.ComboPool.Length);
        }

        [Theory]
        [InlineData("/addbot hard", BotDifficulty.Hard)]
        [InlineData("/addbot dificil 2", BotDifficulty.Hard)]
        [InlineData("/addbot easy", BotDifficulty.Easy)]
        [InlineData("/addbot facil", BotDifficulty.Easy)]
        [InlineData("/addbot", BotDifficulty.Normal)]
        [InlineData("/addbot 3", BotDifficulty.Normal)]
        public void Parse_RecognizesDifficultyTokens(string cmd, BotDifficulty expected)
            => Assert.Equal(expected, BotProfile.Parse(cmd));

        [Fact]
        public void Constructor_AppliesProfileHp()
        {
            var hard = new BotPlayer(1, "X", 5, 1, team: 1, BotDifficulty.Hard);
            var easy = new BotPlayer(2, "Y", 5, 1, team: 1, BotDifficulty.Easy);
            Assert.Equal(BotProfile.For(BotDifficulty.Hard).MaxHp, hard.MaxHp);
            Assert.Equal(hard.MaxHp, hard.Hp);
            Assert.Equal(BotProfile.For(BotDifficulty.Easy).MaxHp, easy.MaxHp);
            Assert.True(hard.MaxHp > easy.MaxHp);
        }

        [Fact]
        public void MoveToward_AcceleratesGradually_NotInstantTopSpeed()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { X = 0, Z = 0 };
            float speed = bot.Profile.MoveSpeed;

            bot.MoveToward(100f, 0f);          // alvo distante em +X
            float step1 = bot.X;               // deslocamento do 1º tick
            Assert.True(step1 > 0f && step1 < speed, $"1º passo {step1} deve ser < velocidade plena {speed}");

            float before = bot.X;
            bot.MoveToward(100f, 0f);
            float step2 = bot.X - before;      // deslocamento do 2º tick
            Assert.True(step2 > step1, "o movimento deve ACELERAR (2º passo > 1º)");
        }

        [Fact]
        public void MoveToward_RampsTowardProfileSpeed()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { X = 0, Z = 0 };
            for (int i = 0; i < 30; i++) bot.MoveToward(1000f, 0f);   // tempo p/ atingir o regime
            float before = bot.X;
            bot.MoveToward(1000f, 0f);
            float vel = bot.X - before;
            Assert.True(MathF.Abs(vel - bot.Profile.MoveSpeed) < 0.05f, $"velocidade de regime {vel} ≈ {bot.Profile.MoveSpeed}");
        }

        [Fact]
        public void StrafeAround_OrbitsTarget_KeepsRing_AndFacesTarget()
        {
            const float ring = 2.6f;
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { X = ring, Z = 0f };
            float startX = bot.X, startZ = bot.Z;
            for (int i = 0; i < 10; i++) bot.StrafeAround(0f, 0f, dir: 1, ring);

            float dist = MathF.Sqrt(bot.X * bot.X + bot.Z * bot.Z);
            Assert.InRange(dist, ring - 1.2f, ring + 1.2f);              // mantém ~o anel (orbita, não foge/cola)
            float moved = MathF.Sqrt((bot.X - startX) * (bot.X - startX) + (bot.Z - startZ) * (bot.Z - startZ));
            Assert.True(moved > 1.0f, "deslocou-se ao longo do anel (orbitou o alvo)");

            // encara o alvo na origem (o Yaw é do início do tick → lag de ~1 passo no anel, ~9°): tolera 20°
            float wantYaw = MathF.Atan2(0f - bot.X, 0f - bot.Z) * (180f / MathF.PI);
            float diff = MathF.Abs(((bot.Yaw - wantYaw + 540f) % 360f) - 180f);
            Assert.True(diff < 20f, $"mantém o rosto no alvo enquanto orbita (diff {diff:F1}°)");
        }

        [Fact]
        public void AddBot_ThreadsDifficultyIntoBot()
        {
            var f = new Field(1) { Mode = 1, MaxRounds = 1, MinLevel = 1, MaxLevel = 10 };
            var added = f.AddBot("Hardy", 5, 1, team: 1, BotDifficulty.Hard);
            Assert.NotNull(added);
            Assert.Equal(BotDifficulty.Hard, added!.Value.Bot.Difficulty);
            Assert.Equal(BotProfile.For(BotDifficulty.Hard).MaxHp, added.Value.Bot.MaxHp);
        }
    }
}
