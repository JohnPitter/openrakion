using System.Diagnostics;
using RakionClientRuntime;

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

        SuspendedClientProcess process = LegacyClientProcess.StartSuspended(
            new LegacyClientStartOptions(exe, binDir, [user, hexPass, serverId]));
        return (process.ProcessId, process.PrimaryThread);
    }

    internal static string BuildEnvironmentBlock()
    {
        return LegacyClientProcess.BuildEnvironmentBlock();
    }

    private static void ValidateToken(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Argumento do cliente deve ser um token sem espaços", parameter);
    }

    /// <summary>Resume a thread primária do jogo (depois de aplicado o patch) e fecha o handle.</summary>
    public static void Resume(IntPtr hThread) =>
        LegacyClientProcess.Resume(new SuspendedClientProcess(0, hThread));

    /// <summary>Converte a senha em hex ASCII (o esquema que o cliente/world esperam no argv[1]).</summary>
    public static string HexPass(string pass) => LegacyClientCredentials.EncodeAsciiHex(pass);

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
