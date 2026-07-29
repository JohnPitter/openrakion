using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World;

public sealed partial class WorldServer
{
    private const byte ServerConfirmedCombatCause = 8;

    /// <summary>
    /// Recebe TODA animação do humano: as de ataque abrem janela de golpe (na borda de subida) e
    /// as demais encerram o golpe em curso. Sem ver as não-ataque o servidor não sabe quando o
    /// jogador soltou o botão.
    /// </summary>
    internal bool RegisterHumanAnimation(
        ClientSession sender,
        GameplayAnimationAction animation)
    {
        Field? field = GetField(sender.FieldId);
        if (field == null)
            return false;
        lock (field.SyncRoot)
        {
            PlayerRec? attacker = field.FindRec(sender);
            if (attacker == null)
                return false;
            if (animation.Kind != PlayerAnimationKind.Attack)
            {
                attacker.Combat.ReleaseAttackAnimation();
                return false;
            }
            return field.State == 2 &&
                field.Phase == MatchPhase.Playing &&
                attacker.Combat.TryOpenAttack(
                    animation.Header.Sequence,
                    Environment.TickCount64,
                    animation.Argument0);
        }
    }

    private void ResolveNativeBotCombat(Field field)
    {
        List<BotCombatOutcome> outcomes = [];
        List<BotHumanCombatOutcome> humanOutcomes = [];
        List<HumanRespawnOutcome> humanRespawns = [];
        List<byte[]> botRespawnVitals = [];
        bool botsRespawned;
        lock (field.SyncRoot)
        {
            long now = Environment.TickCount64;
            botsRespawned = RespawnDueBots(field, now, botRespawnVitals);
            CaptureHumanRespawns(field, now, humanRespawns);
            foreach (PlayerRec attacker in field.Slots)
            {
                BotCombatOutcome? outcome = TryResolveHumanHit(
                    field, attacker, now);
                if (outcome.HasValue)
                    outcomes.Add(outcome.Value);
                BotHumanCombatOutcome? humanOutcome = TryResolveBotHit(
                    field, attacker, now);
                if (humanOutcome.HasValue)
                    humanOutcomes.Add(humanOutcome.Value);
            }
        }

        foreach (BotCombatOutcome outcome in outcomes)
            PublishCombatOutcome(field, outcome);
        foreach (BotHumanCombatOutcome outcome in humanOutcomes)
            PublishHumanCombatOutcome(field, outcome);
        foreach (HumanRespawnOutcome respawn in humanRespawns)
            PublishHumanRespawn(field, respawn);
        foreach (byte[] vitals in botRespawnVitals)
            PublishBotGameplay(field, vitals);
        if (botsRespawned || outcomes.Count > 0)
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
        byte victimSeat = (byte)hit.BotRecord.Slot;
        // Captura humano×humano: quem apanha publica sobre SI MESMO o `RemainHP` e, no mesmo
        // instante, a reação `0x0311 kind=2`. `EPlayerDamage` NÃO aparece uma vez sequer numa
        // partida completa entre dois clientes — o cliente resolve o próprio dano e só replica o
        // resultado. Para o bot ser desenhado apanhando ele precisa falar esse par, não o evento
        // de dano sintético, que o cliente ignora quando a entidade é remota.
        byte[] vitalsEvent = ServerCombatDatagrams.Vitals(
            new ServerVitalsEvent(
                victimSeat,
                victimSeat,
                ++bot.MoveSeq),
            bot.Health,
            0);
        byte[] reactionPacket = BotMovement.SynthesizeDamage(
            victimSeat,
            ++bot.MoveSeq,
            bot.NextDamageReaction(hit.Died));
        BotVector direction =
            (hit.BotRecord.Position - attacker.Position).Normalized();
        byte[]? deathEvent = hit.Died
            ? ServerCombatDatagrams.Death(
                new ServerDeathEvent(
                    victimSeat,
                    victimSeat,
                    ++bot.MoveSeq,
                    direction))
            : null;
        byte[]? deathBody = hit.Died
            ? ResolveBotDeath(field, attacker, hit.BotRecord, bot, now)
            : null;
        return new BotCombatOutcome(
            (byte)attacker.Slot,
            victimSeat,
            damage,
            bot.Health,
            vitalsEvent,
            reactionPacket,
            deathEvent,
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

    private static BotHumanCombatOutcome? TryResolveBotHit(
        Field field,
        PlayerRec attacker,
        long now)
    {
        BotPlayer? bot = attacker.Bot;
        if (bot == null)
            return null;
        int damage = BotHumanDamagePolicy.ResolveMelee(bot);
        if (!BotCombat.TryResolveBotAttack(
                field, attacker, now, damage, out BotHumanCombatHit hit))
            return null;

        PlayerRec victim = hit.HumanRecord;
        BotVector direction = (victim.Position - attacker.Position).Normalized();
        byte[] animation = BotMovement.SynthesizeDamage(
            (byte)victim.Slot,
            ++bot.MoveSeq,
            bot.NextDamageReaction(hit.Damage.Died));
        byte[] damageEvent = ServerCombatDatagrams.Damage(
            new ServerDamageEvent(
                (byte)attacker.Slot,
                (byte)victim.Slot,
                ++bot.MoveSeq,
                hit.Damage.Damage,
                direction));
        byte[] vitalsEvent = ServerCombatDatagrams.Vitals(
            new ServerVitalsEvent(
                (byte)attacker.Slot,
                (byte)victim.Slot,
                ++bot.MoveSeq),
            victim.Vitals);
        byte[]? deathEvent = null;
        byte[]? deathBody = null;
        if (hit.Damage.Died)
        {
            deathEvent = ServerCombatDatagrams.Death(
                new ServerDeathEvent(
                    (byte)attacker.Slot,
                    (byte)victim.Slot,
                    ++bot.MoveSeq,
                    direction));
            deathBody = ResolveHumanDeath(field, attacker, victim, now);
        }
        return new BotHumanCombatOutcome(
            (byte)attacker.Slot,
            (byte)victim.Slot,
            hit.Damage,
            animation,
            damageEvent,
            vitalsEvent,
            deathEvent,
            deathBody);
    }

    private static byte[] ResolveHumanDeath(
        Field field,
        PlayerRec attacker,
        PlayerRec victim,
        long now)
    {
        DeathReportResult result = field.ApplyReportedDeath(
            victim.Slot,
            attacker.Slot,
            ServerConfirmedCombatCause);
        if (!result.Processed)
            throw new InvalidOperationException(
                "Morte autoritativa do humano não foi aceita pelo field.");
        victim.Dead = true;
        victim.Vitals.ScheduleRespawn(
            now, BotRespawnPolicy.DelayMs(field.Mode));
        return FieldLifecycleFrames.Death(
            (byte)victim.Slot,
            ServerConfirmedCombatCause,
            (byte)attacker.Slot,
            result.ScoreA,
            result.ScoreB);
    }

    private static bool RespawnDueBots(
        Field field,
        long now,
        List<byte[]> vitals)
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
            // Sem republicar os vitais, a barra do bot fica vazia depois do respawn.
            vitals.Add(ServerCombatDatagrams.Vitals(
                new ServerVitalsEvent(
                    (byte)record.Slot,
                    (byte)record.Slot,
                    ++record.Bot.MoveSeq),
                record.Bot.Health,
                0));
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

    private static void CaptureHumanRespawns(
        Field field,
        long now,
        List<HumanRespawnOutcome> outcomes)
    {
        if (field.State != 2 || field.Phase != MatchPhase.Playing)
            return;
        foreach (PlayerRec record in field.Slots)
        {
            if (record.Session == null ||
                !record.Dead ||
                record.Vitals.RespawnAtMs == 0 ||
                now < record.Vitals.RespawnAtMs)
                continue;
            PlayerRec? source = field.RecAt(
                record.Vitals.LastDamageSourceSeat);
            BotPlayer? bot = source?.Bot;
            if (bot == null || !record.Vitals.TryRespawn(now))
                continue;
            record.Dead = false;
            record.State = 4;
            outcomes.Add(new HumanRespawnOutcome(
                (byte)record.Slot,
                ServerCombatDatagrams.Respawn(
                    (byte)record.Slot, ++bot.MoveSeq),
                ServerCombatDatagrams.Vitals(
                    new ServerVitalsEvent(
                        (byte)source!.Slot,
                        (byte)record.Slot,
                        ++bot.MoveSeq),
                    record.Vitals)));
        }
    }

    private void PublishCombatOutcome(Field field, BotCombatOutcome outcome)
    {
        // Ordem medida no fio da vítima humana: vitais primeiro, reação no mesmo instante, morte
        // por último. Inverter faz o cliente desenhar o recuo antes de saber o HP novo.
        PublishBotGameplay(field, outcome.VitalsEvent);
        PublishBotGameplay(field, outcome.ReactionPacket);
        if (outcome.DeathEvent != null)
            PublishBotGameplay(field, outcome.DeathEvent);
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

    private void PublishHumanCombatOutcome(
        Field field,
        BotHumanCombatOutcome outcome)
    {
        PublishBotGameplay(field, outcome.Animation);
        PublishBotGameplay(field, outcome.DamageEvent);
        PublishBotGameplay(field, outcome.VitalsEvent);
        if (outcome.DeathEvent != null)
            PublishBotGameplay(field, outcome.DeathEvent);
        if (outcome.DeathBody != null)
        {
            field.BroadcastFieldPlaying(0x4f, outcome.DeathBody);
            if (field.Phase == MatchPhase.RoundEnd)
                field.BroadcastFieldPlaying(0x4a, field.Build0x4a());
        }
        Log.Ok(
            "bot-combat",
            "field={0} bot={1} victim={2} damage={3} hp={4} ap={5} died={6}",
            field.Id,
            outcome.BotSeat,
            outcome.VictimSeat,
            outcome.Damage.Damage,
            outcome.Damage.RemainingHp,
            outcome.Damage.RemainingAp,
            outcome.Damage.Died);
    }

    private void PublishHumanRespawn(
        Field field,
        HumanRespawnOutcome outcome)
    {
        PublishBotGameplay(field, outcome.RespawnEvent);
        PublishBotGameplay(field, outcome.VitalsEvent);
        Log.Ok(
            "bot-combat",
            "field={0} humano={1} respawn autoritativo",
            field.Id,
            outcome.VictimSeat);
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
        byte[] VitalsEvent,
        byte[] ReactionPacket,
        byte[]? DeathEvent,
        byte[]? DeathBody,
        bool Died);

    private readonly record struct BotHumanCombatOutcome(
        byte BotSeat,
        byte VictimSeat,
        PlayerDamageResult Damage,
        byte[] Animation,
        byte[] DamageEvent,
        byte[] VitalsEvent,
        byte[]? DeathEvent,
        byte[]? DeathBody);

    private readonly record struct HumanRespawnOutcome(
        byte VictimSeat,
        byte[] RespawnEvent,
        byte[] VitalsEvent);
}
