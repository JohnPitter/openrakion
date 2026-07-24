using System.Diagnostics;
using System.Runtime.InteropServices;
using RakionClientRuntime;

namespace RakionBotHost;

internal static class Program
{
    private const int HideWindow = 0;
    private const uint WindowActivate = 0x0006;
    private const uint KillFocus = 0x0008;
    private const uint ActivateApplication = 0x001C;

    public static int Main(string[] args)
    {
        SuspendedClientProcess suspended = default;
        try
        {
            BotHostOptions options = BotHostOptions.Parse(args);
            IntPtr foregroundWindow = GetForegroundWindow();
            using ChildProcessJob job = ChildProcessJob.Create();
            suspended = Start(options);
            using Process process = Process.GetProcessById(suspended.ProcessId);
            job.Assign(process);
            LegacyClientProcess.Resume(suspended);
            suspended = default;
            Console.WriteLine(
                $"bot-host field={options.FieldId} pid={process.Id} dedicated=1 iniciado");
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            };
            Console.CancelKeyPress += cancel;
            try { Monitor(process, options.FieldId, foregroundWindow); }
            finally { Console.CancelKeyPress -= cancel; }
            return process.ExitCode;
        }
        catch (Exception error)
        {
            if (suspended.PrimaryThread != IntPtr.Zero)
                LegacyClientProcess.Abort(suspended);
            Console.Error.WriteLine($"bot-host falhou: {error.Message}");
            return 1;
        }
    }

    private static SuspendedClientProcess Start(BotHostOptions options)
    {
        string bin = Path.Combine(options.ClientRoot, "Bin");
        string executable = Path.Combine(bin, "rakion.exe");
        ValidateCompatibility(bin);
        var environment = new Dictionary<string, string>
        {
            ["OPENRAKION_HEADLESS"] = "1",
            ["OPENRAKION_HEADLESS_FIELD"] = options.FieldId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [BotHostOptions.WorldVariable] = options.WorldName
        };
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BotHostOptions.CredentialVariable
        };
        return LegacyClientProcess.StartSuspended(new LegacyClientStartOptions(
            executable, bin,
            [options.User, LegacyClientCredentials.EncodeAsciiHex(options.Credential),
                options.ServerId],
            environment, excluded));
    }

    private static void ValidateCompatibility(string bin)
    {
        foreach (string file in new[] { "version.dll", "RakionClientPatch.dll" })
        {
            string path = Path.Combine(bin, file);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Compatibilidade ausente: {file}", path);
        }
    }

    private static void Monitor(Process process, int fieldId, IntPtr foregroundWindow)
    {
        bool windowHidden = false;
        while (!process.WaitForExit(250))
        {
            process.Refresh();
            IntPtr window = process.MainWindowHandle;
            if (window == IntPtr.Zero) continue;
            IsolateInput(window, foregroundWindow);
            if (windowHidden) continue;
            windowHidden = true;
            Console.WriteLine($"bot-host field={fieldId} shell e input isolados");
        }
        Console.WriteLine($"bot-host field={fieldId} encerrado code={process.ExitCode}");
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
