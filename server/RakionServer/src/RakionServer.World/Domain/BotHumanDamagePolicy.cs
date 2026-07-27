using System;

namespace RakionServer.World.Domain;

public static class BotHumanDamagePolicy
{
    private const int MinimumDamage = 10;
    private const int MaximumDamage = 80;

    public static int ResolveMelee(BotPlayer attacker)
    {
        int difficultyBonus = attacker.Difficulty switch
        {
            BotDifficulty.Easy => 0,
            BotDifficulty.Hard => 10,
            _ => 5
        };
        return Math.Clamp(
            MinimumDamage + attacker.Level + difficultyBonus,
            MinimumDamage,
            MaximumDamage);
    }
}
