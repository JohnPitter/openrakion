using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RakionLauncher;

/// <summary>
/// Lança o rakion.exe DIRETO (sem o load.bin/diálogo) — como o run_rakion.bat/launch_argv0.ps1 do dev,
/// que já roda no ambiente offline. O cliente lê argv[0] como o userID, então a command line PRECISA
/// começar pelo user (não pelo exe): daí o CreateProcess com lpApplicationName separado do lpCommandLine.
/// argv: [0]=user · [1]=hexPass · [2]=serverId. O processo é o rakion.exe; o engine cria a janela "Rakion".
/// </summary>
internal static class GameLauncher
{
    public const string GameProcess = "rakion.exe";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2; public IntPtr Reserved3, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string? cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr hThread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ProcessTerminate = 0x0001;
    private const string CompatibilityLayer = "__COMPAT_LAYER";
    private const string RunAsInvoker = "RunAsInvoker";

    /// <summary>Lança rakion.exe SUSPENSO (argv[0]=user) enquanto o launcher conclui o bootstrap.
    /// A version.dll de compatibilidade é carregada pelo loader antes do entry point e aplica os patches.
    /// Devolve o PID e o handle da thread primária — chame <see cref="Resume"/> ao terminar.</summary>
    public static (int pid, IntPtr hThread) LaunchSuspended(string binDir, string user, string hexPass, string serverId)
    {
        string exe = Path.Combine(binDir, GameProcess);
        if (!File.Exists(exe)) throw new FileNotFoundException("rakion.exe não encontrado", exe);
        ValidateToken(user, nameof(user));
        ValidateToken(hexPass, nameof(hexPass));
        ValidateToken(serverId, nameof(serverId));

        var cmd = new StringBuilder($"{user} {hexPass} {serverId}");
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        IntPtr environment = Marshal.StringToHGlobalUni(BuildEnvironmentBlock());
        try
        {
            uint flags = CreateSuspended | CreateUnicodeEnvironment;
            if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags,
                               environment, binDir, ref si, out var pi))
                throw new InvalidOperationException(
                    $"CreateProcess falhou (err {Marshal.GetLastWin32Error()})");
            CloseHandle(pi.hProcess);
            return (pi.dwProcessId, pi.hThread);
        }
        finally
        {
            Marshal.FreeHGlobal(environment);
        }
    }

    internal static string BuildEnvironmentBlock()
    {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            values[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        values.Remove(PuppetLaunch.PasswordVariable);
        values[CompatibilityLayer] = RunAsInvoker;
        return string.Join('\0', values.Select(pair => $"{pair.Key}={pair.Value}")) + "\0";
    }

    private static void ValidateToken(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Argumento do cliente deve ser um token sem espaços", parameter);
    }

    /// <summary>Resume a thread primária do jogo (depois de aplicado o patch) e fecha o handle.</summary>
    public static void Resume(IntPtr hThread) { ResumeThread(hThread); CloseHandle(hThread); }

    public static void AbortSuspended(int pid, IntPtr hThread)
    {
        CloseHandle(hThread);
        IntPtr process = OpenProcess(ProcessTerminate, false, pid);
        if (process == IntPtr.Zero) return;
        try { TerminateProcess(process, 1); }
        finally { CloseHandle(process); }
    }

    /// <summary>Converte a senha em hex ASCII (o esquema que o cliente/world esperam no argv[1]).</summary>
    public static string HexPass(string pass) => Convert.ToHexString(Encoding.ASCII.GetBytes(pass)).ToLowerInvariant();

    public static int CountRunning(string binDir)
    {
        string expectedExecutable = Path.GetFullPath(Path.Combine(binDir, GameProcess));
        int count = 0;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameProcess)))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && IsGameExecutable(process.MainModule?.FileName, expectedExecutable))
                        count++;
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        return count;
    }

    internal static bool IsGameExecutable(string? executablePath, string expectedExecutable) =>
        !string.IsNullOrWhiteSpace(executablePath) &&
        string.Equals(
            Path.GetFullPath(executablePath),
            Path.GetFullPath(expectedExecutable),
            StringComparison.OrdinalIgnoreCase);
}
