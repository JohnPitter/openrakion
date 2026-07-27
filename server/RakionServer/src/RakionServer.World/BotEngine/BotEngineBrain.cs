using System;
using RakionServer.World.Domain;

namespace RakionServer.World.BotEngine;

internal readonly record struct BotEngineIntent(
    BotEngineAim Aim,
    BotEngineInput Input,
    byte TargetSeat,
    BotControls Controls);

internal static class BotEngineBrain
{
    private const float MinimumAttackDistance = 1.25f;
    private const float AttackRange = 3.25f;

    public static bool TryPlan(
        Field field,
        byte botSeat,
        uint botId,
        long now,
        out BotEngineIntent intent)
    {
        intent = default;
        lock (field.SyncRoot)
        {
            PlayerRec? botRecord = field.RecAt(botSeat);
            BotPlayer? bot = botRecord?.Bot;
            if (botRecord == null || bot == null || !bot.Alive)
                return false;
            bot.TryFinishHitReaction(now);
            if (bot.HitReactionUntilMs != 0)
                return false;
            if (!TryFindNearestEnemy(field, botRecord, out PlayerRec target))
                return false;

            float distance = bot.Position.HorizontalDistanceTo(target.Position);
            BotEngineInput input = ResolveInput(bot, distance, now);
            BotControls controls = ToControls(input);
            // Domínio usa yaw em radianos (mesmo contrato do 0x030A humano). O Aim
            // nativo orienta a engine; o cone de melee autoritativo usa este heading.
            float facing = botRecord.Position.HeadingTo(target.Position);
            bot.Heading = facing;
            botRecord.Heading = facing;
            bot.TargetSeat = (byte)target.Slot;
            bot.SetEngineIntent(
                controls, input.HasFlag(BotEngineInput.PrimaryAttack));
            intent = new BotEngineIntent(
                new BotEngineAim(
                    botId, target.Position.X, target.Position.Y, target.Position.Z),
                input,
                (byte)target.Slot,
                controls);
            return true;
        }
    }

    private static bool TryFindNearestEnemy(
        Field field,
        PlayerRec bot,
        out PlayerRec target)
    {
        target = null!;
        float nearest = float.MaxValue;
        foreach (PlayerRec candidate in field.Slots)
        {
            if (candidate.Session == null ||
                !candidate.Occupied ||
                candidate.Dead ||
                candidate.Team == bot.Team)
                continue;
            float distance = bot.Position.HorizontalDistanceTo(candidate.Position);
            if (distance >= nearest)
                continue;
            nearest = distance;
            target = candidate;
        }
        return target is not null;
    }

    private static BotEngineInput ResolveInput(
        BotPlayer bot,
        float distance,
        long now)
    {
        if (distance > AttackRange)
            return BotEngineInput.Forward;
        if (distance < MinimumAttackDistance)
            return BotEngineInput.Backward;
        if (!bot.TryStartAttack(now))
            return BotEngineInput.None;
        return BotEngineInput.PrimaryAttack;
    }

    private static BotControls ToControls(BotEngineInput input)
    {
        BotControls controls = BotControls.None;
        if (input.HasFlag(BotEngineInput.Forward)) controls |= BotControls.W;
        if (input.HasFlag(BotEngineInput.Backward)) controls |= BotControls.S;
        if (input.HasFlag(BotEngineInput.Left)) controls |= BotControls.A;
        if (input.HasFlag(BotEngineInput.Right)) controls |= BotControls.D;
        if (input.HasFlag(BotEngineInput.Jump)) controls |= BotControls.Space;
        return controls;
    }
}
