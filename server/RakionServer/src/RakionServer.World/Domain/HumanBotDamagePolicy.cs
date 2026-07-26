using System;

namespace RakionServer.World.Domain;

public static class HumanBotDamagePolicy
{
    private const int BaseMeleeDamage = 50;
    private const int MaximumMeleeDamage = 250;

    public static int ResolveMelee(PlayerRec attacker)
    {
        if (attacker.Session == null)
            return 0;
        int allocatedBasicAttack = attacker.Session.Stats[0];
        return Math.Clamp(
            BaseMeleeDamage + allocatedBasicAttack * 2,
            BaseMeleeDamage,
            MaximumMeleeDamage);
    }
}
