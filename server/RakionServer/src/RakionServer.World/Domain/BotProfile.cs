namespace RakionServer.World.Domain
{
    /// <summary>Nível de dificuldade do bot (comando /addbot e botão nativo).</summary>
    public enum BotDifficulty : byte { Easy = 0, Normal = 1, Hard = 2 }

    /// <summary>
    /// Parâmetros de IA do bot por dificuldade. Regra de negócio pura (sem I/O): velocidade e
    /// aceleração do movimento server-side, raio/velocidade de orbita no melee e o fator de
    /// antecipação (EMA da velocidade do alvo). Consumido por <see cref="BotSteering"/>.
    /// </summary>
    public readonly record struct BotProfile(
        float MoveSpeed,        // unidades wire/segundo de deslocamento máximo (100 wire = 1 unidade do mapa)
        float Acceleration,     // suavização da velocidade (0..1 por tick; 1 = instantâneo)
        float MeleeRange,       // distância na qual passa a orbitar em vez de avançar
        float StrafeSpeed,      // velocidade angular da orbita (rad/s) no melee
        float Anticipation,     // peso da antecipação do alvo (0 = mira posição atual)
        float ReactionSeconds)  // atraso de reação antes de re-mirar
    {
        public static BotProfile For(BotDifficulty difficulty) => difficulty switch
        {
            BotDifficulty.Easy => new BotProfile(
                MoveSpeed: 300f, Acceleration: 0.25f, MeleeRange: 250f,
                StrafeSpeed: 0.8f, Anticipation: 0.0f, ReactionSeconds: 0.45f),
            BotDifficulty.Hard => new BotProfile(
                MoveSpeed: 600f, Acceleration: 0.6f, MeleeRange: 200f,
                StrafeSpeed: 2.2f, Anticipation: 0.5f, ReactionSeconds: 0.08f),
            _ => new BotProfile(
                MoveSpeed: 450f, Acceleration: 0.4f, MeleeRange: 220f,
                StrafeSpeed: 1.4f, Anticipation: 0.25f, ReactionSeconds: 0.20f),
        };

        public static BotProfile Normal => For(BotDifficulty.Normal);
    }
}
