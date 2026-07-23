using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace RakionClientRuntime;

public sealed record LegacyClientStartOptions(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? EnvironmentOverrides = null,
    IReadOnlySet<string>? ExcludedEnvironmentVariables = null);

public readonly record struct SuspendedClientProcess(int ProcessId, IntPtr PrimaryThread);

public static class LegacyClientProcess
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ProcessTerminate = 0x0001;

    public static SuspendedClientProcess StartSuspended(LegacyClientStartOptions options)
    {
        Validate(options);
        var commandLine = new StringBuilder(string.Join(' ', options.Arguments));
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        IntPtr environment = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(
            options.EnvironmentOverrides, options.ExcludedEnvironmentVariables));
        try
        {
            if (!CreateProcess(
                    options.ExecutablePath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    CreateSuspended | CreateUnicodeEnvironment, environment,
                    options.WorkingDirectory, ref startup, out ProcessInformation process))
                throw new InvalidOperationException(
                    $"CreateProcess falhou (err {Marshal.GetLastWin32Error()})");
            CloseHandle(process.Process);
            return new SuspendedClientProcess(process.ProcessId, process.Thread);
        }
        finally
        {
            Marshal.FreeHGlobal(environment);
        }
    }

    public static void Resume(SuspendedClientProcess process)
    {
        ResumeThread(process.PrimaryThread);
        CloseHandle(process.PrimaryThread);
    }

    public static void Abort(SuspendedClientProcess process)
    {
        CloseHandle(process.PrimaryThread);
        IntPtr handle = OpenProcess(ProcessTerminate, false, process.ProcessId);
        if (handle == IntPtr.Zero) return;
        try { TerminateProcess(handle, 1); }
        finally { CloseHandle(handle); }
    }

    public static string BuildEnvironmentBlock(
        IReadOnlyDictionary<string, string>? overrides = null,
        IReadOnlySet<string>? excluded = null)
    {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            values[(string)entry.Key] = (string?)entry.Value ?? "";
        if (excluded != null)
            foreach (string name in excluded)
                values.Remove(name);
        if (overrides != null)
            foreach ((string name, string value) in overrides)
                values[name] = value;
        values["__COMPAT_LAYER"] = "RunAsInvoker";
        return string.Join('\0', values.Select(pair => $"{pair.Key}={pair.Value}")) + "\0";
    }

    private static void Validate(LegacyClientStartOptions options)
    {
        if (!File.Exists(options.ExecutablePath))
            throw new FileNotFoundException("rakion.exe não encontrado", options.ExecutablePath);
        if (!Directory.Exists(options.WorkingDirectory))
            throw new DirectoryNotFoundException(options.WorkingDirectory);
        if (options.Arguments.Count == 0 ||
            options.Arguments.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)))
            throw new ArgumentException("Argumentos do cliente devem ser tokens sem espaços.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int CharacterWidth;
        public int CharacterHeight;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr ReservedPointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string currentDirectory, ref StartupInfo startup, out ProcessInformation process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
