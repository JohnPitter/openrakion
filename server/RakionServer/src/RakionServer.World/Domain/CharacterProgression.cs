using System;

namespace RakionServer.World.Domain;

public readonly record struct CharacterProgressionState(long Exp, byte Level, byte LevelPoint);

public static class CharacterProgression
{
    public static uint ApplyCombatExitPenalty(long currentExp, byte level, byte differential)
    {
        uint current = (uint)Math.Clamp(currentExp, 0, uint.MaxValue);
        uint cost = (uint)(level >> 1) + (uint)differential * 5u;
        return cost < current ? current - cost : 0;
    }

    public static CharacterProgressionState Project(
        CharacterProgressionState current, uint awardedExp, Func<byte, int> cumulativeExp)
    {
        long totalExp = checked(current.Exp + awardedExp);
        byte level = current.Level == 0 ? (byte)1 : current.Level;
        int levelPoint = current.LevelPoint;

        while (level < 99)
        {
            int ceiling = cumulativeExp(level);
            if (ceiling <= 0) break;
            int floor = cumulativeExp((byte)(level - 1));
            int threshold = floor + (ceiling - floor) * 2 / 5;
            if (totalExp < threshold) break;
            level++;
            levelPoint += 3;
        }

        return new CharacterProgressionState(
            totalExp, level, (byte)Math.Min(levelPoint, byte.MaxValue));
    }
}
