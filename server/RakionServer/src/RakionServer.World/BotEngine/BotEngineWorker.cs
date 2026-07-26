using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.World.BotEngine;

internal sealed record BotEngineWorkerOptions(
    string HostPath,
    string ClientRoot,
    TimeSpan StartupTimeout,
    TimeSpan ShutdownTimeout);

internal sealed class BotEngineWorker : IAsyncDisposable
{
    private sealed record HostProcess(
        Process Process,
        Task StandardOutput,
        Task StandardError);

    private sealed record LaunchContext(
        BotEngineWorkerOptions Options,
        BotEngineFieldRequest Field,
        string PipeName);

    private readonly BotEngineClient _client;
    private readonly HostProcess _host;
    private readonly TimeSpan _shutdownTimeout;
    private int _stopped;

    private BotEngineWorker(
        BotEngineClient client,
        HostProcess host,
        TimeSpan shutdownTimeout,
        BotEngineFieldRequest field)
    {
        _client = client;
        _host = host;
        _shutdownTimeout = shutdownTimeout;
        Field = field;
        ProcessId = host.Process.Id;
    }

    public BotEngineFieldRequest Field { get; }
    public int ProcessId { get; }
    public bool IsRunning
    {
        get
        {
            if (Volatile.Read(ref _stopped) != 0)
                return false;
            try
            {
                return !_host.Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public static async Task<BotEngineWorker> StartAsync(
        BotEngineWorkerOptions options,
        BotEngineFieldRequest field,
        CancellationToken cancellationToken)
    {
        Validate(options);
        var launch = new LaunchContext(
            options, field, CreatePipeName(field.FieldId));
        HostProcess host = StartProcess(options, launch.PipeName, field.FieldId);
        BotEngineClient? client = null;

        try
        {
            client = await ConnectAndInitializeAsync(
                host, launch, cancellationToken).ConfigureAwait(false);
            Log.Ok("bot-engine", "field={0} host x86 ativo pid={1}",
                field.FieldId, host.Process.Id);
            return new BotEngineWorker(
                client, host, options.ShutdownTimeout, field);
        }
        catch (Exception exception)
        {
            Log.Error("bot-engine", "field={0} falhou ao iniciar host: {1}",
                field.FieldId, exception.Message);
            if (client is not null)
                await client.DisposeAsync().ConfigureAwait(false);
            await TerminateAsync(host).ConfigureAwait(false);
            throw;
        }
    }

    public Task<BotEngineHealth> PingAsync(CancellationToken cancellationToken) =>
        _client.PingAsync(cancellationToken);

    public Task<BotEngineBot> AddBotAsync(
        BotEngineBotRequest request,
        CancellationToken cancellationToken) =>
        _client.AddBotAsync(request, cancellationToken);

    public Task<BotEngineTick> TickAsync(
        uint frameCount,
        CancellationToken cancellationToken) =>
        _client.TickAsync(frameCount, cancellationToken);

    public Task<BotEnginePlayerSnapshot> SnapshotAsync(
        uint botId,
        CancellationToken cancellationToken) =>
        _client.SnapshotAsync(botId, cancellationToken);

    public Task<BotEngineInputResult> ApplyInputAsync(
        uint botId,
        BotEngineInput input,
        CancellationToken cancellationToken) =>
        _client.ApplyInputAsync(botId, input, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_shutdownTimeout);
        try
        {
            if (!_host.Process.HasExited)
                await _client.ShutdownAsync(timeout.Token).ConfigureAwait(false);
            await _host.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException ||
            exception is IOException ||
            exception is BotEngineException)
        {
            Log.Warn("bot-engine", "field={0} encerramento forçado: {1}",
                Field.FieldId, exception.Message);
            Kill(_host.Process);
        }
        finally
        {
            try
            {
                await _client.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await AwaitPumpsAsync(
                    _host.StandardOutput, _host.StandardError).ConfigureAwait(false);
                _host.Process.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<BotEngineClient> ConnectAndInitializeAsync(
        HostProcess host,
        LaunchContext launch,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(launch.Options.StartupTimeout);
        BotEngineClient client = await BotEngineClient.ConnectAsync(
            launch.PipeName,
            launch.Options.StartupTimeout,
            timeout.Token).ConfigureAwait(false);
        try
        {
            BotEngineHello hello = await client.HelloAsync(timeout.Token).ConfigureAwait(false);
            ValidateHello(hello, host.Process.Id);
            await client.LoadFieldAsync(
                launch.Field, timeout.Token).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static HostProcess StartProcess(
        BotEngineWorkerOptions options,
        string pipeName,
        uint fieldId)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(options.HostPath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(options.HostPath))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--client-root");
        start.ArgumentList.Add(Path.GetFullPath(options.ClientRoot));
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        Process process = Process.Start(start) ??
            throw new InvalidOperationException("Bot Engine Host não iniciou.");
        return new HostProcess(
            process,
            PumpAsync(process.StandardOutput, fieldId, false),
            PumpAsync(process.StandardError, fieldId, true));
    }

    private static async Task PumpAsync(
        StreamReader reader,
        uint fieldId,
        bool isError)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (isError)
                Log.Warn("bot-engine", "field={0} host: {1}", fieldId, line);
            else
                Log.Debug("bot-engine", "field={0} host: {1}", fieldId, line);
        }
    }

    private static void Validate(BotEngineWorkerOptions options)
    {
        if (!File.Exists(options.HostPath))
            throw new FileNotFoundException("BotEngineHost.exe não encontrado.",
                options.HostPath);
        if (!Directory.Exists(options.ClientRoot))
            throw new DirectoryNotFoundException(
                $"ClientRoot não encontrado: {options.ClientRoot}");
        if (options.StartupTimeout <= TimeSpan.Zero ||
            options.ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), "Timeouts devem ser positivos.");
    }

    private static void ValidateHello(BotEngineHello hello, int processId)
    {
        const BotEngineProtocol.Capability required =
            BotEngineProtocol.Capability.EngineBootstrap |
            BotEngineProtocol.Capability.NativeWorld |
            BotEngineProtocol.Capability.NativePlayerSources |
            BotEngineProtocol.Capability.NativeSnapshots |
            BotEngineProtocol.Capability.NativeInputs;
        if (hello.ProcessId != unchecked((uint)processId) ||
            hello.ProtocolVersion != BotEngineProtocol.Version ||
            (hello.Capabilities & required) != required)
            throw new InvalidDataException(
                "Handshake do Bot Engine Host não atende ao contrato obrigatório.");
    }

    private static string CreatePipeName(uint fieldId) =>
        $"orbh-{Environment.ProcessId}-{fieldId}-{Guid.NewGuid():N}";

    private static async Task TerminateAsync(HostProcess host)
    {
        Kill(host.Process);
        await AwaitPumpsAsync(
            host.StandardOutput, host.StandardError).ConfigureAwait(false);
        host.Process.Dispose();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task AwaitPumpsAsync(Task output, Task error)
    {
        try
        {
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }
}
