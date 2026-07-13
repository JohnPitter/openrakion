using System.Runtime.InteropServices;

namespace RakionLauncher;

internal static class BotHitCompatibility
{
    private const uint ProcessAccess = 0x0010 | 0x0020 | 0x0008 | 0x0400;
    private const uint PageExecuteReadWrite = 0x40;
    private const int PatchAddress = 0x351533e9;
    private const int ContinueAddress = PatchAddress + 5;
    private const int ReceiveDamageStackReturnOffset = 0x4d4;
    private const int RangedDamageReturnAddress = 0x3519f5ad;
    private static readonly byte[] Expected = { 0x68, 0x30, 0xa6, 0x2b, 0x35 };
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "rakion_bot_hit.log");

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, int size, uint allocationType, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, int size, uint protection, out uint oldProtection);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, int size);

    internal static void InstallWhenReady(int pid)
    {
        IntPtr process = OpenProcess(ProcessAccess, false, (uint)pid);
        if (process == IntPtr.Zero) { Log($"OpenProcess falhou pid={pid} err={Marshal.GetLastWin32Error()}"); return; }
        try
        {
            if (!WaitForCode(process, pid)) return;
            Install(process, pid);
        }
        catch (Exception ex) { Log($"instalação falhou pid={pid}: {ex.Message}"); }
        finally { CloseHandle(process); }
    }

    private static bool WaitForCode(IntPtr process, int pid)
    {
        var current = new byte[Expected.Length];
        for (int attempt = 0; attempt < 1200 && GameLauncher.IsAlive(pid); attempt++)
        {
            if (ReadProcessMemory(process, new IntPtr(PatchAddress), current, current.Length, out var read) &&
                read.ToInt64() == current.Length)
            {
                if (current.AsSpan().SequenceEqual(Expected)) return true;
                if (current[0] == 0xe9) { Log($"pid={pid}: patch já instalado"); return false; }
            }
            Thread.Sleep(100);
        }
        Log($"pid={pid}: entitiesmp.dll não ficou pronta em 120s");
        return false;
    }

    private static void Install(IntPtr process, int pid)
    {
        IntPtr cave = VirtualAllocEx(process, IntPtr.Zero, 0x1000, 0x1000 | 0x2000, PageExecuteReadWrite);
        if (cave == IntPtr.Zero) throw new InvalidOperationException($"VirtualAllocEx err={Marshal.GetLastWin32Error()}");
        byte[] code = BuildCode(unchecked((uint)cave.ToInt64()));
        WriteExact(process, cave, code);
        byte[] detour = BuildJump(PatchAddress, unchecked((uint)cave.ToInt64()));
        if (!VirtualProtectEx(process, new IntPtr(PatchAddress), detour.Length, PageExecuteReadWrite, out uint old))
            throw new InvalidOperationException($"VirtualProtectEx err={Marshal.GetLastWin32Error()}");
        try { WriteExact(process, new IntPtr(PatchAddress), detour); }
        finally { VirtualProtectEx(process, new IntPtr(PatchAddress), detour.Length, old, out _); }
        FlushInstructionCache(process, new IntPtr(PatchAddress), detour.Length);
        Log($"pid={pid}: compatibilidade HIT/SHOT instalada cave=0x{cave.ToInt64():X}");
    }

    private static byte[] BuildCode(uint cave)
    {
        var code = new List<byte>(128);
        Emit(code, 0x9c, 0x60);
        Emit(code, 0x0f, 0xb6, 0x9e, 0x64, 0x02, 0x00, 0x00);
        Emit(code, 0x83, 0xfb, 0x14);
        int invalidSeat = EmitShortBranch(code, 0x73);
        Emit(code, 0x8b, 0x0d, 0x60, 0xf2, 0x36, 0x36, 0x85, 0xc9);
        int noGame = EmitShortBranch(code, 0x74);
        Emit(code, 0x8b, 0x01, 0xff, 0x50, 0x08);
        Emit(code, 0x69, 0xdb, 0x78, 0x03, 0x00, 0x00);
        Emit(code, 0x0f, 0xb7, 0x94, 0x18, 0xec, 0x01, 0x00, 0x00);
        Emit(code, 0x66, 0x81, 0xfa, 0x9f, 0x04);
        int firstBotPort = EmitShortBranch(code, 0x74);
        Emit(code, 0x66, 0x81, 0xfa, 0x9f, 0x05);
        int otherPort = EmitShortBranch(code, 0x75);
        int botEndpoint = code.Count;
        Emit(code, 0xa1, 0x30, 0x36, 0x2b, 0x35, 0xff, 0xd0);
        Emit(code, 0x3b, 0xe8);
        int remoteAttacker = EmitShortBranch(code, 0x75);
        Emit(code, 0x8b, 0x84, 0x24);
        Emit(code, BitConverter.GetBytes(ReceiveDamageStackReturnOffset));
        Emit(code, 0x3d);
        Emit(code, BitConverter.GetBytes(RangedDamageReturnAddress));
        Emit(code, 0x75, 0x04, 0x6a, 0x0a, 0xeb, 0x02, 0x6a, 0x01, 0x8b, 0xcd);
        Emit(code, 0xb8, 0xe0, 0x3c, 0x15, 0x35, 0xff, 0xd0);
        int cleanup = code.Count;
        PatchShortBranch(code, invalidSeat, cleanup);
        PatchShortBranch(code, noGame, cleanup);
        PatchShortBranch(code, firstBotPort, botEndpoint);
        PatchShortBranch(code, otherPort, cleanup);
        PatchShortBranch(code, remoteAttacker, cleanup);
        Emit(code, 0x61, 0x9d);
        Emit(code, Expected);
        EmitJump(code, cave + (uint)code.Count, ContinueAddress);
        return code.ToArray();
    }

    private static byte[] BuildJump(uint source, uint target)
    {
        var bytes = new List<byte>(5);
        EmitJump(bytes, source, target);
        return bytes.ToArray();
    }

    private static void EmitJump(List<byte> bytes, uint source, uint target)
    {
        bytes.Add(0xe9);
        bytes.AddRange(BitConverter.GetBytes(unchecked((int)(target - (source + 5)))));
    }

    private static void Emit(List<byte> bytes, params byte[] values) => bytes.AddRange(values);

    private static int EmitShortBranch(List<byte> bytes, byte opcode)
    {
        bytes.Add(opcode);
        bytes.Add(0);
        return bytes.Count - 1;
    }

    private static void PatchShortBranch(List<byte> bytes, int displacementIndex, int target)
    {
        int displacement = target - (displacementIndex + 1);
        if (displacement is < sbyte.MinValue or > sbyte.MaxValue)
            throw new InvalidOperationException("branch do code-cave excedeu 8 bits");
        bytes[displacementIndex] = unchecked((byte)(sbyte)displacement);
    }

    private static void WriteExact(IntPtr process, IntPtr address, byte[] bytes)
    {
        if (!WriteProcessMemory(process, address, bytes, bytes.Length, out var written) || written.ToInt64() != bytes.Length)
            throw new InvalidOperationException($"WriteProcessMemory @0x{address.ToInt64():X} err={Marshal.GetLastWin32Error()}");
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"); } catch { }
    }
}
