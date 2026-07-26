using System;

namespace RakionServer.World.Domain;

public readonly record struct BotCombatHit(
    PlayerRec BotRecord,
    uint HitSequence,
    bool Died);

public static class BotCombat
{
    public const float MeleeRange = 3.25f;
    private const float MaximumVerticalDistance = 2f;
    private const float FrontalDotThreshold = 0.258819f;

    public static bool TryResolveHumanAttack(
        Field field,
        PlayerRec attacker,
        long nowMs,
        int damage,
        out BotCombatHit hit)
    {
        hit = default;
        if (!CanAttack(field, attacker, damage) ||
            !attacker.Combat.TryGetActiveAttack(nowMs, out PlayerAttackWindow attack))
            return false;

        PlayerRec? target = FindNearestTarget(field, attacker);
        if (target?.Bot == null)
            return false;
        uint hitSequence = attacker.Combat.ConfirmHit(attack.Sequence);
        if (hitSequence == 0)
            return false;

        bool died = target.Bot.TakeDamage(
            damage, (byte)attacker.Slot, hitSequence);
        hit = new BotCombatHit(target, hitSequence, died);
        return true;
    }

    private static bool CanAttack(Field field, PlayerRec attacker, int damage) =>
        field.State == 2 &&
        field.Phase == MatchPhase.Playing &&
        attacker.Playing &&
        !attacker.Dead &&
        attacker.Bot == null &&
        damage > 0;

    private static PlayerRec? FindNearestTarget(Field field, PlayerRec attacker)
    {
        PlayerRec? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (PlayerRec candidate in field.BotSlots)
        {
            BotPlayer bot = candidate.Bot!;
            float vertical = MathF.Abs(candidate.Position.Y - attacker.Position.Y);
            float distance = candidate.Position.HorizontalDistanceTo(attacker.Position);
            if (!bot.EngineAttached ||
                !bot.Alive ||
                !candidate.Playing ||
                candidate.Dead ||
                candidate.Team == attacker.Team ||
                vertical > MaximumVerticalDistance ||
                distance > MeleeRange ||
                distance >= nearestDistance ||
                !IsInsideFrontalCone(attacker, candidate.Position, distance))
                continue;
            nearest = candidate;
            nearestDistance = distance;
        }
        return nearest;
    }

    private static bool IsInsideFrontalCone(
        PlayerRec attacker,
        BotVector targetPosition,
        float distance)
    {
        if (distance <= 0.01f)
            return true;
        float dx = (targetPosition.X - attacker.Position.X) / distance;
        float dz = (targetPosition.Z - attacker.Position.Z) / distance;
        float dot = MathF.Sin(attacker.Heading) * dx +
            MathF.Cos(attacker.Heading) * dz;
        return dot >= FrontalDotThreshold;
    }
}
