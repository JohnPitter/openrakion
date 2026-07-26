using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World;

public sealed partial class WorldServer
{
    private const byte ServerConfirmedCombatCause = 8;

    internal bool RegisterHumanBotAttack(
        ClientSession sender,
        GameplayAnimationAction attack)
    {
        Field? field = GetField(sender.FieldId);
        if (field == null)
            return false;
        lock (field.SyncRoot)
        {
            PlayerRec? attacker = field.FindRec(sender);
            return attacker != null &&
                field.State == 2 &&
                field.Phase == MatchPhase.Playing &&
                attacker.Combat.TryOpenAttack(
                    attack.Header.Sequence,
                    Environment.TickCount64);
        }
    }

    private void ResolveNativeBotCombat(Field field)
    {
        List<BotCombatOutcome> outcomes = [];
        bool respawned;
        lock (field.SyncRoot)
        {
            long now = Environment.TickCount64;
            respawned = RespawnDueBots(field, now);
            foreach (PlayerRec attacker in field.Slots)
            {
                BotCombatOutcome? outcome = TryResolveHumanHit(
                    field, attacker, now);
                if (outcome.HasValue)
                    outcomes.Add(outcome.Value);
            }
        }

        foreach (BotCombatOutcome outcome in outcomes)
            PublishCombatOutcome(field, outcome);
        if (respawned || outcomes.Count > 0)
            Bots.PublishBotLifecycles(field);
    }

    private static BotCombatOutcome? TryResolveHumanHit(
        Field field,
        PlayerRec attacker,
        long now)
    {
        int damage = HumanBotDamagePolicy.ResolveMelee(attacker);
        if (!BotCombat.TryResolveHumanAttack(
                field, attacker, now, damage, out BotCombatHit hit))
            return null;

        BotPlayer bot = hit.BotRecord.Bot!;
        bot.BeginHitReaction(now);
        byte[] damagePacket = BotMovement.SynthesizeDamage(
            (byte)hit.BotRecord.Slot,
            ++bot.MoveSeq,
            (byte)attacker.Slot);
        byte[]? deathBody = hit.Died
            ? ResolveBotDeath(field, attacker, hit.BotRecord, bot, now)
            : null;
        return new BotCombatOutcome(
            (byte)attacker.Slot,
            (byte)hit.BotRecord.Slot,
            damage,
            bot.Health,
            damagePacket,
            deathBody,
            hit.Died);
    }

    private static byte[] ResolveBotDeath(
        Field field,
        PlayerRec attacker,
        PlayerRec victim,
        BotPlayer bot,
        long now)
    {
        DeathReportResult result = field.ApplyReportedDeath(
            victim.Slot,
            attacker.Slot,
            ServerConfirmedCombatCause);
        if (!result.Processed)
            throw new InvalidOperationException(
                "Morte confirmada do bot não foi aceita pelo field.");
        bot.ScheduleRespawn(now, BotRespawnPolicy.DelayMs(field.Mode));
        return FieldLifecycleFrames.Death(
            (byte)victim.Slot,
            ServerConfirmedCombatCause,
            (byte)attacker.Slot,
            result.ScoreA,
            result.ScoreB);
    }

    private static bool RespawnDueBots(Field field, long now)
    {
        if (field.State != 2 || field.Phase != MatchPhase.Playing)
            return false;
        bool changed = false;
        foreach (PlayerRec record in field.BotSlots)
        {
            if (!record.Bot!.TryRespawn(now))
                continue;
            record.Dead = false;
            record.State = 4;
            changed = true;
            Log.Ok(
                "bot-combat",
                "field={0} bot={1} respawn hp={2}/{3}",
                field.Id,
                record.Slot,
                record.Bot.Health,
                record.Bot.MaxHealth);
        }
        return changed;
    }

    private void PublishCombatOutcome(Field field, BotCombatOutcome outcome)
    {
        PublishBotGameplay(field, outcome.DamagePacket);
        if (outcome.DeathBody != null)
        {
            field.BroadcastFieldPlaying(0x4f, outcome.DeathBody);
            if (field.Phase == MatchPhase.RoundEnd)
                field.BroadcastFieldPlaying(0x4a, field.Build0x4a());
        }
        Log.Ok(
            "bot-combat",
            "field={0} attacker={1} bot={2} damage={3} hp={4} died={5}",
            field.Id,
            outcome.AttackerSeat,
            outcome.BotSeat,
            outcome.Damage,
            outcome.RemainingHealth,
            outcome.Died);
    }

    private void PublishBotGameplay(Field field, byte[] packet)
    {
        if (_udpGame == null)
            return;
        List<PlayerRec> humans = [];
        lock (field.SyncRoot)
            foreach (PlayerRec record in field.Slots)
                if (record.Session != null && record.Occupied)
                    humans.Add(record);
        foreach (PlayerRec human in humans)
            _udpGame.SendBotGameplay(human, packet);
    }

    private readonly record struct BotCombatOutcome(
        byte AttackerSeat,
        byte BotSeat,
        int Damage,
        int RemainingHealth,
        byte[] DamagePacket,
        byte[]? DeathBody,
        bool Died);
}
