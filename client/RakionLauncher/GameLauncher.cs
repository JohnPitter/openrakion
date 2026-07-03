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

    // ---- Injeção de DLL no cliente (capture_hook.dll) — CEDO, como parent (sem UAC; antes do anti-tamper armar).
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, uint size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, uint size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr attr, uint stack, IntPtr start, IntPtr param, uint flags, out int tid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr h, string name);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, int pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Module32FirstW(IntPtr snap, ref MODULEENTRY32 me);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Module32NextW(IntPtr snap, ref MODULEENTRY32 me);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32
    {
        public uint dwSize; public uint th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
        public IntPtr modBaseAddr; public uint modBaseSize; public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    // Acha a base do módulo (ex.: kernel32.dll) DENTRO do processo alvo (x86 via snapshot 32-bit).
    private static IntPtr RemoteModuleBase(int pid, string moduleName)
    {
        const uint TH32CS_SNAPMODULE = 0x8, TH32CS_SNAPMODULE32 = 0x10;
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snap == (IntPtr)(-1)) return IntPtr.Zero;
        var me = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
        for (bool ok = Module32FirstW(snap, ref me); ok; ok = Module32NextW(snap, ref me))
            if (string.Equals(me.szModule, moduleName, StringComparison.OrdinalIgnoreCase)) { CloseHandle(snap); return me.modBaseAddr; }
        CloseHandle(snap); return IntPtr.Zero;
    }

    // Resolve o endereço de uma export NO ALVO lendo a export table do módulo (necessário cross-bitness:
    // o LoadLibraryW x86 do rakion.exe != o x64 do launcher). Lê via ReadProcessMemory.
    private static IntPtr RemoteProcAddress(IntPtr hProc, IntPtr modBase, string func)
    {
        uint R4(int off) { var b = new byte[4]; ReadProcessMemory(hProc, modBase + off, b, 4, out _); return BitConverter.ToUInt32(b, 0); }
        uint e_lfanew = R4(0x3C);
        uint expDirRva = R4((int)e_lfanew + 0x78);          // DataDirectory[0] (export) RVA (PE32)
        uint nNames = R4((int)(expDirRva + 0x18));
        uint addrFuncsRva = R4((int)(expDirRva + 0x1C));
        uint addrNamesRva = R4((int)(expDirRva + 0x20));
        uint addrOrdsRva = R4((int)(expDirRva + 0x24));
        for (uint i = 0; i < nNames; i++)
        {
            uint nameRva = R4((int)(addrNamesRva + i * 4));
            var nb = new byte[func.Length + 1];
            ReadProcessMemory(hProc, modBase + (int)nameRva, nb, nb.Length, out _);
            string name = Encoding.ASCII.GetString(nb, 0, func.Length);
            if (name == func && nb[func.Length] == 0)
            {
                var ob = new byte[2]; ReadProcessMemory(hProc, modBase + (int)(addrOrdsRva + i * 2), ob, 2, out _);
                ushort ord = BitConverter.ToUInt16(ob, 0);
                uint funcRva = R4((int)(addrFuncsRva + ord * 4));
                return modBase + (int)funcRva;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Injeta uma DLL no processo (LoadLibraryW via CreateRemoteThread), resolvendo o LoadLibraryW DO ALVO
    /// (cross-bitness: launcher x64 → rakion.exe x86). Devolve string de status (diagnóstico).</summary>
    public static string InjectDll(int pid, string dllPath)
    {
        const uint PROCESS_ALL_ACCESS = 0x1F0FFF, MEM_COMMIT_RESERVE = 0x3000, PAGE_RW = 4;
        IntPtr h = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
        if (h == IntPtr.Zero) return $"OpenProcess err {Marshal.GetLastWin32Error()}";
        IntPtr k32 = RemoteModuleBase(pid, "kernel32.dll");
        if (k32 == IntPtr.Zero) return "kernel32 do alvo nao achado (modulos ainda carregando?)";
        IntPtr loadLib = RemoteProcAddress(h, k32, "LoadLibraryW");
        if (loadLib == IntPtr.Zero) return "LoadLibraryW do alvo nao resolvido";
        byte[] path = Encoding.Unicode.GetBytes(dllPath + "\0");
        IntPtr mem = VirtualAllocEx(h, IntPtr.Zero, (uint)path.Length, MEM_COMMIT_RESERVE, PAGE_RW);
        if (mem == IntPtr.Zero) return $"VirtualAllocEx err {Marshal.GetLastWin32Error()}";
        if (!WriteProcessMemory(h, mem, path, (uint)path.Length, out _)) return $"WriteProcessMemory err {Marshal.GetLastWin32Error()}";
        IntPtr t = CreateRemoteThread(h, IntPtr.Zero, 0, loadLib, mem, 0, out _);
        if (t == IntPtr.Zero) return $"CreateRemoteThread err {Marshal.GetLastWin32Error()}";
        return $"ok (LoadLibraryW@{loadLib.ToInt64():X})";
    }

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

    /// <summary>O processo de PID dado ainda está vivo? Monitoramento POR INSTÂNCIA (multi-cliente): cada launch
    /// acompanha o seu próprio rakion.exe, em vez de "existe algum rakion?" — que confundiria 2 clientes.</summary>
    public static bool IsAlive(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }   // pid inexistente -> já saiu
    }
}
