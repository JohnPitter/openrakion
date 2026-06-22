using System;
using System.Collections.Generic;

namespace RakionServer.World.Database
{
    /// <summary>
    /// Configuração do refino/enchant, lida do banco no boot (tabelas `enchant_catalyzer` + `enchant_config`,
    /// editáveis pelo painel admin). Tira os coeficientes de dentro do código: dá pra rodar EVENTOS (multiplicador
    /// global de sucesso) e atrelar bônus ao Power User sem recompilar. A estrutura da fórmula é fiel à decompilação
    /// (CUser::CheckEnchantReinforce / FUN_0040c310); só os números viram dado configurável.
    /// </summary>
    public sealed class EnchantConfig
    {
        /// <summary>Coeficientes de um catalisador: base de sucesso (nível 0), decaimento por nível e o nível
        /// MÁXIMO da arma que ele aceita (curLevel ≤ cap; acima disso o refino recusa com erro 8).</summary>
        public readonly record struct Catalyzer(double BaseSuccess, double Decay, int LevelCap);

        private readonly Dictionary<int, Catalyzer> _catalyzers = new();

        // Globais (defaults de fábrica; o boot sobrescreve com a linha enchant_config id=1).
        public double JewelFloor = 0.05;   // piso de sucesso por joia tipo 3 (Charm 0x36b3)
        public double JewelBonus = 0.03;   // bônus de sucesso por joia tipo 1/2 (Abradant/Soul Stone)
        public double EventMult = 1.0;     // multiplicador GLOBAL de sucesso (eventos) — 1.0 = neutro
        public double PuMult = 1.0;        // multiplicador de sucesso quando o jogador tem Power User — 1.0 = neutro
        public double FloorMin = 0.05;     // clamp inferior da chance final
        public double CeilMax = 0.98;      // clamp superior da chance final
        public double DowngradeLo = 0.12;  // fração das falhas que viram -1 nível, em armas +3..+5
        public double DowngradeHi = 0.30;  // fração das falhas que viram -1 nível, em armas +6 ou mais

        public void SetCatalyzer(int id, Catalyzer cat) => _catalyzers[id] = cat;
        public bool TryGetCatalyzer(int id, out Catalyzer cat) => _catalyzers.TryGetValue(id, out cat);
        public int CatalyzerCount => _catalyzers.Count;

        /// <summary>Probabilidade de SUCESSO (subir +1), já com multiplicadores de evento/PU e o clamp final.
        /// Polinômio por nível fiel ao FUN_0040c310: a joia tipo 3 dá um piso, as tipo 1/2 dão bônus aditivo.
        /// 0 se o catalisador não existe (refino recusado a montante).</summary>
        public double SuccessChance(int catalyzerId, int curLevel, int j1, int j2, int j3, bool puActive)
        {
            if (!_catalyzers.TryGetValue(catalyzerId, out var cat)) return 0.0;
            double floor = j3 * JewelFloor;
            double keep = 1.0 - floor;
            double p = keep * cat.BaseSuccess + floor;
            for (int lv = 1; lv <= curLevel; lv++)
                p = (1.0 - lv * cat.Decay) * keep * p + floor;
            p += (j1 + j2) * JewelBonus;
            p *= EventMult;
            if (puActive) p *= PuMult;
            return Math.Clamp(p, FloorMin, CeilMax);
        }

        /// <summary>Fração das falhas que vira downgrade (-1 nível) no nível dado. Armas baixas não caem.</summary>
        public double DowngradeFactor(int curLevel) =>
            curLevel >= 6 ? DowngradeHi : curLevel >= 3 ? DowngradeLo : 0.0;
    }
}
