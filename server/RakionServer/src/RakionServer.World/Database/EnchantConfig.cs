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

        public int Version { get; set; } = 1;
        public bool UseOriginalOutcomes { get; set; } = true;

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
            float floor = j3 * (float)JewelFloor;
            float keep = 1.0f - floor;
            float baseSuccess = (float)cat.BaseSuccess;
            float decay = (float)cat.Decay;
            double p = (double)keep * baseSuccess + floor;
            for (int lv = 1; lv <= curLevel; lv++)
                p = (1.0 - (double)lv * decay) * keep * p + floor;
            if (UseOriginalOutcomes) p = (float)p;
            if (!UseOriginalOutcomes) p += (j1 + j2) * JewelBonus;
            p *= EventMult;
            if (puActive) p *= PuMult;
            return UseOriginalOutcomes
                ? (float)Math.Clamp(p, 0.0, 1.0)
                : Math.Clamp(p, FloorMin, CeilMax);
        }

        public float[] OriginalOutcomeProbabilities(
            int catalyzerId, int curLevel, int jewel1, int jewel2, int jewel3, bool puActive)
        {
            float success = (float)SuccessChance(
                catalyzerId, curLevel, jewel1, jewel2, jewel3, puActive);
            double failure = 1.0 - success;
            return
            [
                success,
                (float)(failure * (4.0f + 0.2f * jewel1 + 1.2 * jewel2) / 15.0),
                (float)(failure * (3.0f + 0.6f * jewel2) * (1.0f / 15.0f)),
                (float)(failure * (2.0f + 0.4f * jewel2) * (1.0f / 15.0f)),
                (float)(failure * (1.0f + 0.1f * jewel2) * (1.0f / 15.0f)),
                (float)(failure * (1.0f - 0.2f * jewel1) * (1.0f / 15.0f))
            ];
        }

        public double DowngradeFactor(int curLevel) =>
            curLevel >= 6 ? DowngradeHi : curLevel >= 3 ? DowngradeLo : 0.0;
    }
}
