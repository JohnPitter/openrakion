using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.BotEngine;

internal sealed class BotEngineCoordinator : IAsyncDisposable
{
    private sealed class FieldSession
    {
        public FieldSession(BotEngineWorker worker)
        {
            Worker = worker;
        }

        public BotEngineWorker Worker { get; }
        public ConcurrentDictionary<byte, uint> BotIds { get; } = new();
    }

    private readonly WorldConfig.BotEngineConfig _config;
    private readonly BotEngineSupervisor _supervisor;
    private readonly ConcurrentDictionary<int, FieldSession> _sessions = new();

    public BotEngineCoordinator(WorldConfig.BotEngineConfig config)
    {
        _config = config;
        _supervisor = new BotEngineSupervisor(new BotEngineWorkerOptions(
            config.HostPath,
            config.ClientRoot,
            TimeSpan.FromSeconds(config.StartupTimeoutSeconds),
            TimeSpan.FromSeconds(config.ShutdownTimeoutSeconds)));
    }

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
    }

    public async Task<bool> TickFieldAsync(
        Field field,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(field.Id, out FieldSession? session))
            return true;
        try
        {
            await session.Worker.TickAsync(1, cancellationToken).ConfigureAwait(false);
            foreach (KeyValuePair<byte, uint> entry in session.BotIds)
            {
                BotEnginePlayerSnapshot snapshot = await session.Worker.SnapshotAsync(
                    entry.Value, cancellationToken).ConfigureAwait(false);
                ApplySnapshot(field, entry.Key, snapshot);
            }
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
            record.Bot.ApplyEngineTransform(position, snapshot.RotationY);
            record.Position = position;
            record.Heading = snapshot.RotationY;
        }
    }
}
