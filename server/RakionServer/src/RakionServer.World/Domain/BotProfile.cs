namespace RakionServer.World.Domain
{
    /// <summary>Nível de dificuldade do bot (comando /addbot e botão nativo).</summary>
    public enum BotDifficulty : byte { Easy = 0, Normal = 1, Hard = 2 }

    /// <summary>
    /// Parâmetros de cadência do bot por dificuldade. Movimento e colisão vêm do Bot Engine Host;
    /// o domínio só controla cooldown de ataque e política de dano.
    /// </summary>
    public readonly record struct BotProfile(int AttackCooldownMs)
    {
        public static BotProfile For(BotDifficulty difficulty) => difficulty switch
        {
            BotDifficulty.Easy => new BotProfile(2200),
            BotDifficulty.Hard => new BotProfile(1300),
            _ => new BotProfile(1700),
        };

        public static BotProfile Normal => For(BotDifficulty.Normal);
    }
}
