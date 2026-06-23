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

    private const uint CREATE_SUSPENDED = 0x00000004;

    /// <summary>Lança rakion.exe SUSPENSO (argv[0]=user) pra dar tempo de aplicar o patch do modo janela
    /// (ver <see cref="WindowMode.PatchWindowedMode"/>) ANTES do engine inicializar o display e trocar a
    /// resolução. Devolve o PID e o handle da thread primária — chame <see cref="Resume"/> depois de patchar.</summary>
    public static (int pid, IntPtr hThread) LaunchSuspended(string binDir, string user, string hexPass, string serverId)
    {
        string exe = Path.Combine(binDir, GameProcess);
        if (!File.Exists(exe)) throw new FileNotFoundException("rakion.exe não encontrado", exe);

        var cmd = new StringBuilder($"{user} {hexPass} {serverId}");   // argv[0]=user (sem o exe)
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_SUSPENDED, IntPtr.Zero, binDir, ref si, out var pi))
            throw new InvalidOperationException($"CreateProcess falhou (err {Marshal.GetLastWin32Error()})");
        CloseHandle(pi.hProcess);
        return (pi.dwProcessId, pi.hThread);
    }

    /// <summary>Resume a thread primária do jogo (depois de aplicado o patch) e fecha o handle.</summary>
    public static void Resume(IntPtr hThread) { ResumeThread(hThread); CloseHandle(hThread); }

    /// <summary>Converte a senha em hex ASCII (o esquema que o cliente/world esperam no argv[1]).</summary>
    public static string HexPass(string pass) => Convert.ToHexString(Encoding.ASCII.GetBytes(pass)).ToLowerInvariant();

    public static bool IsRunning()
    {
        foreach (var _ in System.Diagnostics.Process.GetProcessesByName("rakion")) return true;
        return false;
    }
}
