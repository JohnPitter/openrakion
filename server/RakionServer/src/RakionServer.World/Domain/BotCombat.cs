using System;

namespace RakionServer.World.Domain;

public readonly record struct BotCombatHit(
    PlayerRec BotRecord,
    uint HitSequence,
    bool Died);

public readonly record struct BotHumanCombatHit(
    PlayerRec HumanRecord,
    uint HitSequence,
    PlayerDamageResult Damage);

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
        if (!field.IsCombatActive(nowMs) ||
            !CanAttack(attacker, damage) ||
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

    public static bool TryResolveBotAttack(
        Field field,
        PlayerRec attacker,
        long nowMs,
        int damage,
        out BotHumanCombatHit hit)
    {
        hit = default;
        BotPlayer? bot = attacker.Bot;
        if (!field.IsCombatActive(nowMs) ||
            !CanBotAttack(attacker, bot, damage) ||
            !bot!.Combat.TryGetActiveAttack(
                nowMs, out PlayerAttackWindow attack))
            return false;

        PlayerRec? target = field.RecAt(bot.TargetSeat);
        if (!IsValidHumanTarget(attacker, target) ||
            !IsInsideHitbox(attacker, target!))
            return false;
        uint hitSequence = bot.Combat.ConfirmHit(attack.Sequence);
        if (hitSequence == 0)
            return false;

        PlayerDamageResult result = target!.Vitals.ApplyDamage(
            damage, (byte)attacker.Slot);
        hit = new BotHumanCombatHit(target, hitSequence, result);
        return true;
    }

    private static bool CanAttack(PlayerRec attacker, int damage) =>
        attacker.Playing &&
        !attacker.Dead &&
        attacker.Bot == null &&
        damage > 0;

    private static bool CanBotAttack(
        PlayerRec attacker,
        BotPlayer? bot,
        int damage) =>
        attacker.Playing &&
        !attacker.Dead &&
        bot?.EngineAttached == true &&
        bot.Alive &&
        bot.HitReactionUntilMs == 0 &&
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

    private static bool IsValidHumanTarget(
        PlayerRec attacker,
        PlayerRec? target) =>
        target is { Bot: null } &&
        target.Playing &&
        !target.Dead &&
        target.Team != attacker.Team &&
        target.Vitals.Alive;

    private static bool IsInsideHitbox(
        PlayerRec attacker,
        PlayerRec target)
    {
        float vertical = MathF.Abs(
            target.Position.Y - attacker.Position.Y);
        float distance = target.Position.HorizontalDistanceTo(
            attacker.Position);
        return vertical <= MaximumVerticalDistance &&
            distance <= MeleeRange &&
            IsInsideFrontalCone(attacker, target.Position, distance);
    }
}
