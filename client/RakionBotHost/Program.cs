using System.Diagnostics;
using System.Runtime.InteropServices;
using RakionClientRuntime;

namespace RakionBotHost;

internal static class Program
{
    private const int HideWindow = 0;

    public static int Main(string[] args)
    {
        SuspendedClientProcess suspended = default;
        try
        {
            BotHostOptions options = BotHostOptions.Parse(args);
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
            try { Monitor(process, options.FieldId); }
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
                System.Globalization.CultureInfo.InvariantCulture)
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

    private static void Monitor(Process process, int fieldId)
    {
        bool windowHidden = false;
        while (!process.WaitForExit(250))
        {
            process.Refresh();
            IntPtr window = process.MainWindowHandle;
            if (window == IntPtr.Zero) continue;
            ShowWindow(window, HideWindow);
            if (windowHidden) continue;
            windowHidden = true;
            Console.WriteLine($"bot-host field={fieldId} shell ocultado");
        }
        Console.WriteLine($"bot-host field={fieldId} encerrado code={process.ExitCode}");
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
