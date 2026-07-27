using System;

namespace RakionServer.World.Domain;

public readonly record struct HumanCombatMaximums(int Hp, int Ap);

public static class HumanCombatVitalsPolicy
{
    private const int BaseVital = 100;
    private const int MaximumVital = 5000;

    public static HumanCombatMaximums Resolve(
        byte level,
        ushort allocatedEnergy,
        ushort allocatedArmor)
    {
        int hp = ResolveVital(level, allocatedEnergy);
        int ap = ResolveVital(level, allocatedArmor);
        return new HumanCombatMaximums(hp, ap);
    }

    private static int ResolveVital(byte level, ushort allocated) =>
        Math.Clamp(BaseVital + level + allocated, BaseVital, MaximumVital);
}
