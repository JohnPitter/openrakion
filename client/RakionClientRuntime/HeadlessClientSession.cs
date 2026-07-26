using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RakionClientRuntime;

public enum HeadlessClientRole
{
    Joiner,
    Master
}

public sealed record HeadlessClientOptions(
    string ClientRoot,
    string User,
    string Credential,
    string ServerId,
    HeadlessClientRole Role,
    string WorldName,
    string? RoomName = null,
    int? FieldId = null);

public sealed class HeadlessClientSession : IDisposable
{
    private const int HideWindow = 0;
    private const uint WindowActivate = 0x0006;
    private const uint KillFocus = 0x0008;
    private const uint ActivateApplication = 0x001C;

    private readonly Process _process;
    private readonly ChildProcessJob _job;
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly Task _monitorTask;
    private bool _disposed;

    private HeadlessClientSession(
        Process process, ChildProcessJob job, IntPtr foregroundWindow)
    {
        _process = process;
        _job = job;
        _monitorTask = MonitorWindowAsync(foregroundWindow, _monitorCancellation.Token);
    }

    public int ProcessId => _process.Id;
    public bool HasExited
    {
        get
        {
            _process.Refresh();
            return _process.HasExited;
        }
    }
    public int ExitCode => _process.ExitCode;

    public static HeadlessClientSession Start(HeadlessClientOptions options)
    {
        Validate(options);
        SuspendedClientProcess suspended = default;
        ChildProcessJob? job = null;
        try
        {
            suspended = StartSuspended(options);
            var process = Process.GetProcessById(suspended.ProcessId);
            job = ChildProcessJob.Create();
            job.Assign(process);
            IntPtr foregroundWindow = GetForegroundWindow();
            LegacyClientProcess.Resume(suspended);
            suspended = default;
            return new HeadlessClientSession(process, job, foregroundWindow);
        }
        catch
        {
            job?.Dispose();
            if (suspended.PrimaryThread != IntPtr.Zero)
                LegacyClientProcess.Abort(suspended);
            throw;
        }
    }

    public void WaitForExit() => _process.WaitForExit();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitorCancellation.Cancel();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        _monitorTask.GetAwaiter().GetResult();
        _job.Dispose();
        _monitorCancellation.Dispose();
        _process.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SuspendedClientProcess StartSuspended(HeadlessClientOptions options)
    {
        string bin = Path.Combine(options.ClientRoot, "Bin");
        var environment = new Dictionary<string, string>
        {
            ["OPENRAKION_HEADLESS"] = "1",
            ["OPENRAKION_HEADLESS_ROLE"] = options.Role.ToString().ToLowerInvariant(),
            ["OPENRAKION_HEADLESS_WORLD"] = options.WorldName
        };
        if (options.Role == HeadlessClientRole.Master)
        {
            environment["OPENRAKION_HEADLESS_ROOM"] = options.RoomName!;
            environment["OPENRAKION_HEADLESS_MAP"] =
                BattleMapCatalog.Resolve(options.WorldName)
                    .ToString(CultureInfo.InvariantCulture);
        }
        else if (options.FieldId is int fieldId)
            environment["OPENRAKION_HEADLESS_FIELD"] =
                fieldId.ToString(CultureInfo.InvariantCulture);
        else
            environment["OPENRAKION_HEADLESS_QUICK_JOIN"] = "1";

        return LegacyClientProcess.StartSuspended(new LegacyClientStartOptions(
            Path.Combine(bin, "rakion.exe"), bin,
            [options.User, LegacyClientCredentials.EncodeAsciiHex(options.Credential),
                options.ServerId],
            environment,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "OPENRAKION_BOT_CREDENTIAL",
                "OPENRAKION_HEADLESS_MASTER_CREDENTIAL",
                "OPENRAKION_HEADLESS_JOINER_CREDENTIAL"
            }));
    }

    private static void Validate(HeadlessClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.User) ||
            string.IsNullOrWhiteSpace(options.Credential))
            throw new ArgumentException("Usuário e credencial headless são obrigatórios.");
        if (!IsValidWorld(options.WorldName))
            throw new ArgumentException(
                "O world deve apontar para LevelsSV\\...\\map.wld.");
        if (options.Role == HeadlessClientRole.Master &&
            (options.RoomName?.Length is not (>= 1 and <= 40) ||
             options.RoomName.Any(char.IsControl)))
            throw new ArgumentException("A sala master deve ter entre 1 e 40 caracteres.");
        if (options.FieldId is <= 0 or > ushort.MaxValue)
            throw new ArgumentException("O field deve estar entre 1 e 65535.");

        string bin = Path.Combine(options.ClientRoot, "Bin");
        foreach (string file in new[] { "rakion.exe", "version.dll", "RakionClientPatch.dll" })
            if (!File.Exists(Path.Combine(bin, file)))
                throw new FileNotFoundException($"Cliente incompatível: {file} ausente.");
    }

    private static bool IsValidWorld(string world)
    {
        string normalized = world.Replace('/', '\\');
        return normalized.StartsWith("LevelsSV\\", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".wld", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(normalized);
    }

    private async Task MonitorWindowAsync(
        IntPtr foregroundWindow, CancellationToken cancellationToken)
    {
        try
        {
            while (!_process.HasExited)
            {
                _process.Refresh();
                IntPtr window = _process.MainWindowHandle;
                if (window != IntPtr.Zero) IsolateInput(window, foregroundWindow);
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void IsolateInput(IntPtr window, IntPtr foregroundWindow)
    {
        EnableWindow(window, false);
        PostMessage(window, WindowActivate, IntPtr.Zero, IntPtr.Zero);
        PostMessage(window, KillFocus, IntPtr.Zero, IntPtr.Zero);
        PostMessage(window, ActivateApplication, IntPtr.Zero, IntPtr.Zero);
        ShowWindow(window, HideWindow);
        if (foregroundWindow != IntPtr.Zero && GetForegroundWindow() == window)
            SetForegroundWindow(foregroundWindow);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr window, bool enabled);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
