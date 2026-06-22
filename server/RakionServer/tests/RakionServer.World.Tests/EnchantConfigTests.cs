using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Regra de probabilidade do refino (<see cref="EnchantConfig.SuccessChance"/>), agora configurável via banco.
    /// Fixa a ESTRUTURA da fórmula (polinômio por nível, fiel ao FUN_0040c310) e o efeito dos multiplicadores de
    /// evento e Power User — o que permite rodar eventos / bônus de PU mexendo só no DB, sem recompilar.
    /// </summary>
    public class EnchantConfigTests
    {
        private static EnchantConfig Seed()
        {
            var c = new EnchantConfig();
            c.SetCatalyzer(0x32c9, new EnchantConfig.Catalyzer(0.95, 0.10, 4));   // Mithril
            c.SetCatalyzer(0x32cb, new EnchantConfig.Catalyzer(0.90, 0.07, 14));  // Orehalcon
            return c;
        }

        [Fact]
        public void Mithril_BaseChance_DecaysPerLevel()
        {
            var c = Seed();
            Assert.Equal(0.95, c.SuccessChance(0x32c9, 0, 0, 0, 0, false), 3);
            Assert.Equal(0.855, c.SuccessChance(0x32c9, 1, 0, 0, 0, false), 3);   // (1-0.10)*0.95
            Assert.Equal(0.684, c.SuccessChance(0x32c9, 2, 0, 0, 0, false), 3);   // (1-0.20)*0.855
        }

        [Fact]
        public void UnknownCatalyzer_IsZero() =>
            Assert.Equal(0.0, Seed().SuccessChance(0x9999, 0, 0, 0, 0, false));

        [Fact]
        public void EventMultiplier_ScalesChance()
        {
            var c = Seed();
            double baseP = c.SuccessChance(0x32cb, 6, 0, 0, 0, false);
            c.EventMult = 1.5;
            Assert.Equal(baseP * 1.5, c.SuccessChance(0x32cb, 6, 0, 0, 0, false), 4);   // evento +50%, sem clamp nesse nível
        }

        [Fact]
        public void PuMultiplier_OnlyAppliesWhenPuActive()
        {
            var c = Seed();
            c.PuMult = 1.5;
            double noPu = c.SuccessChance(0x32cb, 6, 0, 0, 0, false);
            double withPu = c.SuccessChance(0x32cb, 6, 0, 0, 0, true);
            Assert.Equal(noPu, c.SuccessChance(0x32cb, 6, 0, 0, 0, false), 4);   // sem PU: inalterado
            Assert.Equal(noPu * 1.5, withPu, 4);                                  // com PU: +50%
        }

        [Fact]
        public void CharmJewel_RaisesChance()
        {
            var c = Seed();
            double noJewel = c.SuccessChance(0x32cb, 6, 0, 0, 0, false);
            double withCharm = c.SuccessChance(0x32cb, 6, 0, 0, 1, false);   // +1 Charm (joia tipo 3 = piso)
            Assert.True(withCharm > noJewel);
        }

        [Fact]
        public void Clamp_RespectsCeil()
        {
            var c = Seed();
            c.EventMult = 5.0;   // estoura o teto
            Assert.Equal(0.98, c.SuccessChance(0x32c9, 0, 0, 0, 0, false), 3);   // clamp em CeilMax
        }

        [Theory]
        [InlineData(0, 0.0)]
        [InlineData(2, 0.0)]
        [InlineData(3, 0.12)]
        [InlineData(5, 0.12)]
        [InlineData(6, 0.30)]
        [InlineData(10, 0.30)]
        public void DowngradeFactor_ByLevel(int level, double expected) =>
            Assert.Equal(expected, Seed().DowngradeFactor(level));
    }
}
