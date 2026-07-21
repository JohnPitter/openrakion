namespace RakionServer.World.Domain
{
    /// <summary>Nível de dificuldade do bot (comando /addbot e botão nativo).</summary>
    public enum BotDifficulty : byte { Easy = 0, Normal = 1, Hard = 2 }

    /// <summary>
    /// Parâmetros de IA do bot por dificuldade. Regra de negócio pura (sem I/O): velocidade e
    /// aceleração do movimento server-side, distância de combate, antecipação e cadência.
    /// </summary>
    public readonly record struct BotProfile(
        float MoveSpeed,        // unidades wire/segundo de deslocamento máximo (100 wire = 1 unidade do mapa)
        float Acceleration,     // suavização da velocidade (0..1 por tick; 1 = instantâneo)
        float MeleeRange,       // distância na qual para de perseguir
        float MeleeSpacing,     // distância mínima antes de recuar para não colar no alvo
        float Anticipation,     // peso da antecipação do alvo (0 = mira posição atual)
        int AttackCooldownMs)   // intervalo mínimo entre animações de ataque
    {
        public static BotProfile For(BotDifficulty difficulty) => difficulty switch
        {
            BotDifficulty.Easy => new BotProfile(
                MoveSpeed: 300f, Acceleration: 0.25f, MeleeRange: 250f,
                MeleeSpacing: 120f, Anticipation: 0.0f, AttackCooldownMs: 2200),
            BotDifficulty.Hard => new BotProfile(
                MoveSpeed: 600f, Acceleration: 0.6f, MeleeRange: 200f,
                MeleeSpacing: 100f, Anticipation: 0.5f, AttackCooldownMs: 1300),
            _ => new BotProfile(
                MoveSpeed: 450f, Acceleration: 0.4f, MeleeRange: 220f,
                MeleeSpacing: 110f, Anticipation: 0.25f, AttackCooldownMs: 1700),
        };

        public static BotProfile Normal => For(BotDifficulty.Normal);
    }
}
