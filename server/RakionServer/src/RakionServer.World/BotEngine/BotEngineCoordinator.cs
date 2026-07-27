using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.BotEngine;

internal sealed class BotEngineCoordinator(WorldConfig.BotEngineConfig config) : IAsyncDisposable
{
    private sealed class FieldSession(BotEngineWorker worker)
    {
        public BotEngineWorker Worker { get; } = worker;
        public ConcurrentDictionary<byte, uint> BotIds { get; } = new();
        public ConcurrentDictionary<byte, uint> LifecycleSequences { get; } = new();
        public ConcurrentDictionary<byte, uint> DamageSequences { get; } = new();
    }

    private readonly WorldConfig.BotEngineConfig _config = config;
    private readonly BotEngineSupervisor _supervisor = new BotEngineSupervisor(new BotEngineWorkerOptions(
            config.HostPath,
            config.ClientRoot,
            TimeSpan.FromSeconds(config.StartupTimeoutSeconds),
            TimeSpan.FromSeconds(config.ShutdownTimeoutSeconds)));
    private readonly ConcurrentDictionary<int, FieldSession> _sessions = new();

    public bool Enabled => _config.Enabled;

    public async Task AddBotAsync(
        Field field,
        byte seat,
        BotPlayer bot,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
            throw new InvalidOperationException("Bot Engine Host está desativado.");
        byte engineMapId = checked((byte)(field.MapId + 200));
        var request = new BotEngineFieldRequest(
            (uint)field.Id,
            (ushort)_config.MaxBotsPerField,
            engineMapId,
            field.Mode,
            BattleWorldCatalog.Resolve(engineMapId));
        BotEngineWorker worker = await _supervisor.StartFieldAsync(
            request, cancellationToken).ConfigureAwait(false);
        FieldSession session = _sessions.GetOrAdd(
            field.Id, _ => new FieldSession(worker));
        if (!ReferenceEquals(session.Worker, worker))
            throw new InvalidOperationException(
                $"Field {field.Id} possui worker divergente.");

        uint botId = checked((uint)seat + 1);
        await worker.AddBotAsync(
            new BotEngineBotRequest(botId, bot.Name, "Human"),
            cancellationToken).ConfigureAwait(false);
        if (!session.BotIds.TryAdd(seat, botId))
            throw new InvalidOperationException(
                $"Seat {seat} já possui player nativo.");
        session.LifecycleSequences[seat] = bot.LifecycleSequence;
        session.DamageSequences[seat] = bot.DamageSequence;
    }

    public async Task<bool> TickFieldAsync(
        Field field,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(field.Id, out FieldSession? session))
            return true;
        try
        {
            await SynchronizeDamageReactionsAsync(
                field, session, cancellationToken).ConfigureAwait(false);
            await SynchronizeLifecyclesAsync(
                field, session, cancellationToken).ConfigureAwait(false);
            await session.Worker.TickAsync(1, cancellationToken).ConfigureAwait(false);
            foreach (KeyValuePair<byte, uint> entry in session.BotIds)
            {
                BotEnginePlayerSnapshot snapshot = await session.Worker.SnapshotAsync(
                    entry.Value, cancellationToken).ConfigureAwait(false);
                ApplySnapshot(field, entry.Key, snapshot);
            }
            await ApplyIntentsAsync(
                field, session, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            Log.Error("bot-engine", "field={0} perdeu host nativo: {1}",
                field.Id, exception.Message);
            await StopFieldAsync(field.Id, CancellationToken.None).ConfigureAwait(false);
            return false;
        }
    }

    public async Task StopFieldAsync(
        int fieldId,
        CancellationToken cancellationToken)
    {
        _sessions.TryRemove(fieldId, out _);
        await _supervisor.StopFieldAsync(
            (uint)fieldId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _sessions.Clear();
        await _supervisor.DisposeAsync().ConfigureAwait(false);
    }

    private static void ApplySnapshot(
        Field field,
        byte seat,
        BotEnginePlayerSnapshot snapshot)
    {
        lock (field.SyncRoot)
        {
            PlayerRec? record = field.RecAt(seat);
            if (record?.Bot == null)
                return;
            var position = new BotVector(snapshot.X, snapshot.Y, snapshot.Z);
            record.Bot.ApplyEngineTransform(position, snapshot.RotationX);
            record.Position = position;
            record.Heading = snapshot.RotationX;
        }
    }

    private static async Task ApplyIntentsAsync(
        Field field,
        FieldSession session,
        CancellationToken cancellationToken)
    {
        long now = Environment.TickCount64;
        foreach (KeyValuePair<byte, uint> entry in session.BotIds)
        {
            if (!BotEngineBrain.TryPlan(
                field, entry.Key, entry.Value, now, out BotEngineIntent intent))
            {
                ClearIntent(field, entry.Key);
                await session.Worker.ApplyInputAsync(
                    entry.Value,
                    BotEngineInput.None,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (ShouldRefreshAim(field, entry.Key, intent))
                await session.Worker.AimAsync(
                    intent.Aim, cancellationToken).ConfigureAwait(false);
            await session.Worker.ApplyInputAsync(
                entry.Value,
                intent.Input,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SynchronizeLifecyclesAsync(
        Field field,
        FieldSession session,
        CancellationToken cancellationToken)
    {
        List<LifecycleCommand> commands = CaptureLifecycleCommands(
            field, session);
        foreach (LifecycleCommand command in commands)
        {
            await session.Worker.SetLifecycleAsync(
                command.BotId,
                command.State,
                cancellationToken).ConfigureAwait(false);
            session.LifecycleSequences[command.Seat] = command.Sequence;
        }
    }

    private static async Task SynchronizeDamageReactionsAsync(
        Field field,
        FieldSession session,
        CancellationToken cancellationToken)
    {
        List<DamageReactionCommand> commands = CaptureDamageReactionCommands(
            field, session);
        foreach (DamageReactionCommand command in commands)
        {
            await session.Worker.ApplyDamageReactionAsync(
                command.BotId,
                command.AttackerSeat,
                cancellationToken).ConfigureAwait(false);
            session.DamageSequences[command.Seat] = command.Sequence;
        }
    }

    private static List<LifecycleCommand> CaptureLifecycleCommands(
        Field field,
        FieldSession session)
    {
        List<LifecycleCommand> commands = [];
        lock (field.SyncRoot)
        {
            foreach (KeyValuePair<byte, uint> entry in session.BotIds)
            {
                BotPlayer? bot = field.RecAt(entry.Key)?.Bot;
                if (bot == null ||
                    session.LifecycleSequences.TryGetValue(
                        entry.Key, out uint applied) &&
                    applied == bot.LifecycleSequence)
                    continue;
                commands.Add(new LifecycleCommand(
                    entry.Key,
                    entry.Value,
                    bot.LifecycleSequence,
                    bot.Alive
                        ? BotEngineLifecycle.Alive
                        : BotEngineLifecycle.Dead));
            }
        }
        return commands;
    }

    private static List<DamageReactionCommand> CaptureDamageReactionCommands(
        Field field,
        FieldSession session)
    {
        List<DamageReactionCommand> commands = [];
        lock (field.SyncRoot)
        {
            foreach (KeyValuePair<byte, uint> entry in session.BotIds)
            {
                BotPlayer? bot = field.RecAt(entry.Key)?.Bot;
                if (bot == null ||
                    bot.DamageSequence == 0 ||
                    bot.LastAttackerSeat == Field.NoSeat ||
                    session.DamageSequences.TryGetValue(
                        entry.Key, out uint applied) &&
                    applied == bot.DamageSequence)
                    continue;
                commands.Add(new DamageReactionCommand(
                    entry.Key,
                    entry.Value,
                    bot.DamageSequence,
                    bot.LastAttackerSeat));
            }
        }
        return commands;
    }

    private static void ClearIntent(Field field, byte seat)
    {
        lock (field.SyncRoot)
            field.RecAt(seat)?.Bot?.SetEngineIntent(BotControls.None, false);
    }

    private static bool ShouldRefreshAim(
        Field field,
        byte seat,
        BotEngineIntent intent)
    {
        lock (field.SyncRoot)
            return field.RecAt(seat)?.Bot?.ShouldRefreshEngineAim(
                intent.TargetSeat,
                new BotVector(intent.Aim.X, intent.Aim.Y, intent.Aim.Z)) == true;
    }

    private readonly record struct LifecycleCommand(
        byte Seat,
        uint BotId,
        uint Sequence,
        BotEngineLifecycle State);

    private readonly record struct DamageReactionCommand(
        byte Seat,
        uint BotId,
        uint Sequence,
        byte AttackerSeat);
}
