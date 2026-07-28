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
        public long LastTickMs { get; set; }
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

    /// <summary>
    /// Sobe o worker do field sem criar bots. O bootstrap da engine e a carga do mundo levam
    /// segundos; adiantá-los na criação da sala tira essa espera do caminho do /addbot.
    /// </summary>
    public async Task WarmUpFieldAsync(Field field, CancellationToken cancellationToken)
    {
        if (!Enabled || !BattleWorldCatalog.Supports(field.MapId))
            return;
        try
        {
            await StartWorkerAsync(field, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warn("bot-engine", "field={0} pré-aquecimento falhou: {1}",
                field.Id, exception.Message);
        }
    }

    private Task<BotEngineWorker> StartWorkerAsync(
        Field field,
        CancellationToken cancellationToken)
    {
        // O 0x3B do cliente já traz o mapa battle no catálogo da engine (200..213); não há tradução.
        var request = new BotEngineFieldRequest(
            (uint)field.Id,
            (ushort)_config.MaxBotsPerField,
            field.MapId,
            field.Mode,
            BattleWorldCatalog.Resolve(field.MapId));
        return _supervisor.StartFieldAsync(request, cancellationToken);
    }

    public async Task AddBotAsync(
        Field field,
        byte seat,
        BotPlayer bot,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
            throw new InvalidOperationException("Bot Engine Host está desativado.");
        BotEngineWorker worker = await StartWorkerAsync(
            field, cancellationToken).ConfigureAwait(false);
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
            await session.Worker.TickAsync(
                ResolveFrames(session), cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// A engine simula em quanta de 50 ms; o relógio da partida chama a cada ~150 ms. Avançar um
    /// único frame por chamada faz o bot andar a 1/3 do tempo real e engasgar. O número de frames
    /// acompanha o tempo real decorrido, com teto para não explodir depois de uma pausa.
    /// </summary>
    private static uint ResolveFrames(FieldSession session)
    {
        const long engineTickMs = 50;
        const long maxFrames = 8;
        long now = Environment.TickCount64;
        long previous = session.LastTickMs;
        session.LastTickMs = now;
        if (previous == 0)
            return 1;
        return (uint)Math.Clamp((now - previous) / engineTickMs, 1, maxFrames);
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
            // Placement nativo devolve ângulos SE em graus; o domínio combat/wire usa radianos
            // com a mesma inversão de frente do codec 0x030A.
            float heading = NormalizeEngineHeading(snapshot.RotationX);
            record.Bot.ApplyEngineTransform(position, heading);
            record.Position = position;
            record.Heading = heading;
        }
    }

    private static float NormalizeEngineHeading(float engineDegrees)
    {
        float radians = engineDegrees * MathF.PI / 180f + MathF.PI;
        while (radians > MathF.PI) radians -= 2f * MathF.PI;
        while (radians < -MathF.PI) radians += 2f * MathF.PI;
        return radians;
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
