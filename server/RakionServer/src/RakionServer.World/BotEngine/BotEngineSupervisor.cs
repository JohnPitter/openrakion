using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.World.BotEngine;

internal sealed class BotEngineSupervisor : IAsyncDisposable
{
    private readonly BotEngineWorkerOptions _options;
    private readonly Dictionary<uint, Task<BotEngineWorker>> _workers = [];
    private readonly object _gate = new();
    private bool _disposed;

    public BotEngineSupervisor(BotEngineWorkerOptions options)
    {
        _options = options;
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _workers.Count;
        }
    }

    public async Task<BotEngineWorker> StartFieldAsync(
        BotEngineFieldRequest field,
        CancellationToken cancellationToken)
    {
        Task<BotEngineWorker> start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_workers.TryGetValue(field.FieldId, out start!))
            {
                start = BotEngineWorker.StartAsync(
                    _options, field, cancellationToken);
                _workers.Add(field.FieldId, start);
            }
        }

        BotEngineWorker worker;
        try
        {
            worker = await start.ConfigureAwait(false);
        }
        catch
        {
            RemoveIfSame(field.FieldId, start);
            throw;
        }
        if (worker.Field != field)
            throw new InvalidOperationException(
                $"Field {field.FieldId} já possui outro contrato de host.");
        return worker;
    }

    public async Task<BotEngineHealth> PingFieldAsync(
        uint fieldId,
        CancellationToken cancellationToken)
    {
        Task<BotEngineWorker> start;
        lock (_gate)
        {
            if (!_workers.TryGetValue(fieldId, out start!))
                throw new KeyNotFoundException(
                    $"Field {fieldId} não possui Bot Engine Host.");
        }
        BotEngineWorker worker = await start.ConfigureAwait(false);
        return await worker.PingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopFieldAsync(
        uint fieldId,
        CancellationToken cancellationToken)
    {
        Task<BotEngineWorker>? start;
        lock (_gate)
        {
            if (!_workers.Remove(fieldId, out start))
                return;
        }
        await StopWorkerAsync(fieldId, start, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        KeyValuePair<uint, Task<BotEngineWorker>>[] workers;
        lock (_gate)
        {
            workers = [.. _workers];
            _workers.Clear();
        }
        foreach (var worker in workers)
            await StopWorkerAsync(
                worker.Key, worker.Value, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        KeyValuePair<uint, Task<BotEngineWorker>>[] workers;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            workers = [.. _workers];
            _workers.Clear();
        }
        foreach (var worker in workers)
            await StopWorkerAsync(
                worker.Key, worker.Value, CancellationToken.None).ConfigureAwait(false);
    }

    private void RemoveIfSame(uint fieldId, Task<BotEngineWorker> expected)
    {
        lock (_gate)
        {
            if (_workers.TryGetValue(fieldId, out Task<BotEngineWorker>? current) &&
                ReferenceEquals(current, expected))
                _workers.Remove(fieldId);
        }
    }

    private static async Task StopWorkerAsync(
        uint fieldId,
        Task<BotEngineWorker> start,
        CancellationToken cancellationToken)
    {
        try
        {
            BotEngineWorker worker = await start.ConfigureAwait(false);
            await worker.StopAsync(cancellationToken).ConfigureAwait(false);
            Log.Info("bot-engine", "field={0} host encerrado", fieldId);
        }
        catch (Exception exception)
        {
            Log.Warn("bot-engine", "field={0} falhou ao encerrar host: {1}",
                fieldId, exception.Message);
        }
    }
}
