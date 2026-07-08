using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RakionLauncher
{
    /// <summary>
    /// Injeção PRECOCE de uma DLL de diagnóstico DEV no launch suspenso — só p/ RE/diagnóstico do próprio
    /// binário do autor (nunca p/ funcionalidade shippada; ver regra sem-ddl-injetada). O QueueUserAPC agenda
    /// LoadLibraryA no thread principal AINDA SUSPENSO: quando o <see cref="GameLauncher.Resume"/> roda, o APC
    /// dispara ANTES do anti-tamper armar (o mesmo timing do capture_hook, que a injeção tardia/LoadLibrary
    /// externo não alcança). Opt-in por env var <c>RAKION_DIAG_DLL</c> = caminho da DLL; sem ela, no-op.
    /// </summary>
    internal static class DiagInject
    {
        private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, PAGE_READWRITE = 0x04;
        private const uint PROCESS_ALL = 0x1F0FFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, uint size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, uint size, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandleA(string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr mod, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint QueueUserAPC(IntPtr pfn, IntPtr hThread, IntPtr data);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        /// <summary>Se <c>RAKION_DIAG_DLL</c> aponta p/ uma DLL, agenda o LoadLibrary dela no thread suspenso
        /// <paramref name="hThread"/>. Chamar ENTRE LaunchSuspended e Resume. Devolve uma linha de status.</summary>
        public static string? MaybeInject(int pid, IntPtr hThread)
        {
            string? dll = Environment.GetEnvironmentVariable("RAKION_DIAG_DLL");
            if (string.IsNullOrWhiteSpace(dll)) return null;
            if (!System.IO.File.Exists(dll)) return $"diag: RAKION_DIAG_DLL não existe ({dll})";

            IntPtr hProc = OpenProcess(PROCESS_ALL, false, pid);
            if (hProc == IntPtr.Zero) return $"diag: OpenProcess falhou (err {Marshal.GetLastWin32Error()})";
            try
            {
                byte[] path = Encoding.ASCII.GetBytes(dll + "\0");
                IntPtr remote = VirtualAllocEx(hProc, IntPtr.Zero, (uint)path.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (remote == IntPtr.Zero) return $"diag: VirtualAllocEx falhou (err {Marshal.GetLastWin32Error()})";
                if (!WriteProcessMemory(hProc, remote, path, (uint)path.Length, out _))
                    return $"diag: WriteProcessMemory falhou (err {Marshal.GetLastWin32Error()})";

                IntPtr loadLib = GetProcAddress(GetModuleHandleA("kernel32.dll"), "LoadLibraryA");
                if (loadLib == IntPtr.Zero) return "diag: GetProcAddress(LoadLibraryA) falhou";
                if (QueueUserAPC(loadLib, hThread, remote) == 0)
                    return $"diag: QueueUserAPC falhou (err {Marshal.GetLastWin32Error()})";
                return $"diag: injeção precoce agendada ({System.IO.Path.GetFileName(dll)}) — dispara no Resume";
            }
            finally { CloseHandle(hProc); }
        }
    }
}
