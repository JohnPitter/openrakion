using System;

namespace RakionServer.World.Database
{
    /// <summary>
    /// Configuração do Power User (tabela `pu_config`, linha única id=1). Editável pelo painel admin,
    /// lida pelo World no boot. Define o preço/bônus da compra e os multiplicadores de XP/gold aplicados
    /// enquanto o PU está ativo. Promoção = multiplicadores alternativos dentro de uma janela de datas.
    /// </summary>
    public sealed class PuConfig
    {
        public int Price = 8000;          // custo da compra (cash)
        public int BonusPoints = 51;      // pontos de bônus concedidos por compra
        public int DurationDays = 30;     // validade do PU somada por compra
        public double ExpMult = 1.5;      // multiplicador de XP com PU ativo (fora de promo)
        public double GoldMult = 1.5;     // multiplicador de gold com PU ativo (fora de promo)
        public bool PromoActive;          // liga a promoção (multiplicadores alternativos)
        public double PromoExpMult = 2.0;
        public double PromoGoldMult = 2.0;
        public DateTime? PromoStart;      // null = sem limite inferior
        public DateTime? PromoEnd;        // null = sem limite superior

        /// <summary>Promoção vigente: flag ligada E dentro da janela (limites nulos = abertos).</summary>
        public bool PromoNow(DateTime now) =>
            PromoActive && (PromoStart is null || now >= PromoStart) && (PromoEnd is null || now <= PromoEnd);

        public double EffectiveExpMult(DateTime now) => PromoNow(now) ? PromoExpMult : ExpMult;
        public double EffectiveGoldMult(DateTime now) => PromoNow(now) ? PromoGoldMult : GoldMult;
    }
}
