using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RakionLauncher;

/// <summary>
/// Modo janela do Rakion — porta da técnica do luxview-launcher (windowmode_windows.go + keyhook_windows.go)
/// pra .NET. A Serious Engine só guarda um bit de fullscreen; o "modo janela bonito" é rodar o engine
/// WINDOWED (m_bActiveFullScreen=0) e REFORMATAR a janela de render (classe "Rakion") por fora via Win32:
///  - windowed:   janela com título, centralizada, client = backbuffer (sem margem preta).
///  - borderless: janela com título no canto, preenchendo a tela (o engine desenha 1:1, não estica).
/// Também destrava o Alt+Tab (patch em keyhook.dll) — tudo sem tocar no executável do jogo.
/// </summary>
internal static class WindowMode
{
    public const string Windowed = "windowed";
    public const string Borderless = "borderless";
    public const string Fullscreen = "fullscreen";

    private const string GameWndClass = "Rakion";   // classe da janela de render do engine

    // ---- user32 ----
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr p);
    [DllImport("user32.dll")] private static extern int EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder buf, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rc);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rc);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int idx);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int idx, IntPtr val);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRect(ref RECT rc, uint style, bool menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hwnd, string s);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int idx);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    private static readonly IntPtr DpiUnaware = new(-1);   // DPI_AWARENESS_CONTEXT_UNAWARE

    // Log de diagnóstico do framing (integração com o cliente) — %TEMP%\rakion_launcher.log.
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "rakion_launcher.log");
    private static void Log(string msg) { try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}"); } catch { } }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const long WS_POPUP = 0x80000000, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000;
    private const long WS_EX_TOPMOST = 0x00000008;
    private const long TitledStyle = WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGE = 0x0020, SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    // ---- achar a janela de render do jogo (não a splash "Loading...") ----
    private static IntPtr FindGameWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint wpid);
            if (wpid != pid || !IsWindowVisible(hwnd)) return true;
            if (ClassNameOf(hwnd) != GameWndClass) return true;
            found = hwnd; return false;
        }, IntPtr.Zero);
        return found;
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static (int w, int h) ClientSize(IntPtr hwnd) { GetClientRect(hwnd, out RECT rc); return (rc.Right - rc.Left, rc.Bottom - rc.Top); }
    private static (int w, int h) ScreenSize() => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    private static uint ProcessId(string procName)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(procName)))
            return (uint)p.Id;
        return 0;
    }

    private static IntPtr WaitForGameWindow(string procName, int tries)
    {
        for (int i = 0; i < tries; i++)
        {
            uint pid = ProcessId(procName);
            if (pid != 0) { var h = FindGameWindow(pid); if (h != IntPtr.Zero) return h; }
            Thread.Sleep(500);
        }
        return IntPtr.Zero;
    }

    /// <summary>Espera a janela de render e a reformata pro modo dado. O client é fixado na resolução
    /// ESCOLHIDA (<paramref name="wantW"/>×<paramref name="wantH"/> = o que está no INI = backbuffer do
    /// engine), não no tamanho intermediário que o init cria. Frama UMA vez, depois do engine estabilizar,
    /// e só re-frama se o engine regredir o estilo — sem brigar por posição (era o que fazia a tela piscar).
    /// No-op em fullscreen (exclusivo não precisa de framing).</summary>
    public static void FrameGameWindow(string procName, string mode, int wantW, int wantH)
    {
        if (mode == Fullscreen) return;
        bool fill = mode == Borderless;
        // Mede/reformata no espaço DPI-unaware do jogo (Serious Engine é DPI-unaware) -> as coordenadas
        // e métricas de tela batem; sem isso, em telas HiDPI a janela fica deslocada/cortada.
        SetThreadDpiAwarenessContext(DpiUnaware);
        new Thread(() => KeepWindowAccessible(procName)) { IsBackground = true }.Start();

        if (WaitForGameWindow(procName, 180) == IntPtr.Zero) return;   // ~90s (MD5/GameGuard antes da janela)

        int cw = 0, ch = 0;
        IntPtr framed = IntPtr.Zero;
        // Roda a SESSÃO INTEIRA: o engine RECRIA a janela de render ao trocar de cena (login -> char select ->
        // sala) e ao reinicializar o display; com um hwnd fixo a gente fica preso no handle morto (vira 0x0).
        // Então re-acha a janela "Rakion" sempre e re-frama a recriada. Janela minimizada pelo usuário: respeita.
        uint pid;
        bool wasIconic = false;
        while ((pid = ProcessId(procName)) != 0)
        {
            IntPtr hwnd = FindGameWindow(pid);
            if (hwnd != IntPtr.Zero)
            {
                bool iconic = IsIconic(hwnd);
                if (iconic)
                {
                    // Ao minimizar, tira o título (vira popup) enquanto a janela está INVISÍVEL: ao restaurar, o
                    // engine vê window==backbuffer e NÃO encolhe o client. (Com o título, o engine encolhe o
                    // backbuffer pela borda a CADA restore -> faixa preta que cresce.) O título volta no restore.
                    if (!wasIconic) StripFrame(hwnd);
                }
                else if (hwnd != framed)   // primeira janela ou recriada: espera estabilizar e frama do zero
                {
                    var (engW, engH) = WaitClientStable(hwnd, procName);
                    cw = wantW >= 320 ? wantW : engW;   // alvo = resolução escolhida (= backbuffer do INI)
                    ch = wantH >= 240 ? wantH : engH;
                    ApplyTitledFrame(hwnd, cw, ch, true, fill);
                    framed = hwnd;
                    Log($"framed hwnd=0x{hwnd.ToInt64():X} engine={engW}x{engH} alvo={cw}x{ch} -> {FmtClient(hwnd)} @{FmtRect(hwnd)}");
                }
                else                       // mesma janela visível: re-frama se divergiu (inclui restaurar de popup)
                {
                    long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                    var (acw, ach) = ClientSize(hwnd);
                    bool lost = (style & WS_CAPTION) == 0;   // restaurou como popup (strippado) -> re-titula
                    bool wrongSize = Math.Abs(acw - cw) > 8 || Math.Abs(ach - ch) > 8;
                    if (lost || wrongSize || WrongPlacement(hwnd, cw, ch, fill, style))
                    {
                        if (wasIconic || lost) Log($"re-frame pós-restore: client={acw}x{ach} alvo={cw}x{ch} lost={(lost ? 1 : 0)} wSize={wrongSize}");
                        ApplyTitledFrame(hwnd, cw, ch, false, fill);
                    }
                }
                wasIconic = iconic;
            }
            Thread.Sleep(300);
        }
    }

    /// <summary>Tira o título (vira popup) — usado enquanto a janela está minimizada pra que, ao restaurar, o
    /// engine veja window==client==backbuffer e não encolha. Sem SetWindowPos: a janela está invisível.</summary>
    private static void StripFrame(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        if ((style & WS_CAPTION) != 0)
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr((style & ~TitledStyle) | WS_POPUP));
    }

    /// <summary>A janela está fora do lugar esperado? Borderless quer o canto (0,0); windowed quer
    /// centralizado. Compara com a posição calculada (com tolerância) — uma janela já correta não dispara.</summary>
    private static bool WrongPlacement(IntPtr hwnd, int cw, int ch, bool fill, long style)
    {
        GetWindowRect(hwnd, out RECT rc);
        if (fill) return rc.Left > 40 || rc.Top > 40;
        RECT r = new() { Left = 0, Top = 0, Right = cw, Bottom = ch };
        AdjustWindowRect(ref r, (uint)(style & 0xFFFFFFFF), false);
        int winW = r.Right - r.Left, winH = r.Bottom - r.Top;
        var (sw, sh) = ScreenSize();
        int ex = Math.Max((sw - winW) / 2, 0), ey = Math.Max((sh - winH) / 2, 0);
        return Math.Abs(rc.Left - ex) > 24 || Math.Abs(rc.Top - ey) > 24;
    }

    private static string FmtClient(IntPtr h) { var (w, c) = ClientSize(h); return $"{w}x{c}"; }
    private static string FmtRect(IntPtr h) { GetWindowRect(h, out RECT r); return $"{r.Left},{r.Top}-{r.Right},{r.Bottom}"; }

    /// <summary>Espera o client da janela ficar estável (mesmo tamanho por 3 leituras) — sinal de que o
    /// engine terminou de criar/redimensionar o display. Devolve esse tamanho (fallback se nunca estabilizar).</summary>
    private static (int w, int h) WaitClientStable(IntPtr hwnd, string procName)
    {
        int lastW = -1, lastH = -1, stable = 0;
        for (int i = 0; i < 40 && ProcessId(procName) != 0 && IsWindow(hwnd); i++)   // até ~10s; aborta se a janela morre
        {
            var (w, h) = ClientSize(hwnd);
            if (w >= 320 && h >= 240 && w == lastW && h == lastH) { if (++stable >= 3) return (w, h); }
            else stable = 0;
            lastW = w; lastH = h;
            Thread.Sleep(250);
        }
        return (lastW >= 320 ? lastW : 800, lastH >= 240 ? lastH : 600);
    }

    private static void ApplyTitledFrame(IntPtr hwnd, int clientW, int clientH, bool activate, bool fill)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        style &= ~WS_POPUP;
        style |= TitledStyle;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
        ClearTopmost(hwnd);
        SetWindowText(hwnd, "Rakion");

        RECT rc = new() { Left = 0, Top = 0, Right = clientW, Bottom = clientH };
        AdjustWindowRect(ref rc, (uint)(style & 0xFFFFFFFF), false);
        int winW = rc.Right - rc.Left, winH = rc.Bottom - rc.Top;
        if (activate) Log($"  ApplyTitledFrame: alvo client={clientW}x{clientH} -> SetWindowPos janela {winW}x{winH} style=0x{style & 0xFFFFFFFF:X8}");
        PlaceWindow(hwnd, winW, winH, activate, fill);

        var (acw, ach) = ClientSize(hwnd);   // corrige o gap residual da borda DWM/tema
        if (acw > 0 && ach > 0 && (acw != clientW || ach != clientH))
        {
            winW += clientW - acw; winH += clientH - ach;
            PlaceWindow(hwnd, winW, winH, activate, fill);
        }
        if (activate) FocusWindow(hwnd);
    }

    private static void PlaceWindow(IntPtr hwnd, int winW, int winH, bool activate, bool fill)
    {
        int x = 0, y = 0; int sw = 0, sh = 0;
        if (!fill) { (sw, sh) = ScreenSize(); x = Math.Max((sw - winW) / 2, 0); y = Math.Max((sh - winH) / 2, 0); }
        uint flags = SWP_FRAMECHANGE | SWP_NOZORDER | SWP_SHOWWINDOW;
        if (!activate) flags |= SWP_NOACTIVATE;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, winW, winH, flags);
        Log($"  PlaceWindow: screen={sw}x{sh} manda x={x} y={y} {winW}x{winH} fill={fill} -> colou rect={FmtRect(hwnd)}");
    }

    private static void ClearTopmost(IntPtr hwnd)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_TOPMOST) != 0)
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static void FocusWindow(IntPtr hwnd) { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }

    /// <summary>Roda a sessão toda só impedindo que o jogo fique always-on-top (pro Alt+Tab raisar outras
    /// janelas). A trava real do Alt+Tab é removida por <see cref="PatchKeyHook"/>.</summary>
    private static void KeepWindowAccessible(string procName)
    {
        uint pid;
        while ((pid = ProcessId(procName)) != 0)   // re-acha a janela (o engine recria ao trocar de cena)
        {
            IntPtr hwnd = FindGameWindow(pid);
            if (hwnd != IntPtr.Zero) ClearTopmost(hwnd);
            Thread.Sleep(500);
        }
    }

    // ---- destravar o Alt+Tab (patch em keyhook.dll) ----
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, int size, uint prot, out uint old);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern bool Module32First(IntPtr snap, ref MODULEENTRY32 me);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern bool Module32Next(IntPtr snap, ref MODULEENTRY32 me);

    private const uint PROCESS_VM_OPERATION = 0x0008, PROCESS_VM_READ = 0x0010, PROCESS_VM_WRITE = 0x0020;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint TH32CS_SNAPMODULE = 0x08, TH32CS_SNAPMODULE32 = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MODULEENTRY32
    {
        public uint dwSize, th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
        public IntPtr modBaseAddr; public uint modBaseSize; public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    // Patch do MODO JANELA no rakion.exe (sem ASLR, ImageBase 0x400000): em 0x40D46D o engine tem
    //   85 C0 (TEST EAX,EAX) / 74 52 (JZ +0x52) / FF15.. (CALL [0x4D0468] = setup de fullscreen + troca de
    // resolução). Trocar o 0x74 (JZ) por 0xEB (JMP curto) força o salto SEMPRE -> pula o CALL -> o engine
    // roda windowed de verdade, sem trocar a resolução do desktop. (É o mesmo patch que o NyxLauncher aplica
    // quando o "Window Mode" está marcado: ver RE do RakionLauncher.Loader.) Aplicado com o jogo suspenso,
    // antes do engine chegar nessa instrução.
    private const int WindowedPatchAddr = 0x40D46D;
    private const byte WindowedJzByte = 0x74, WindowedJmpByte = 0xEB;

    /// <summary>Aplica o patch do modo janela no processo do jogo (deve estar suspenso). Valida o byte
    /// original (0x74) antes de escrever — build diferente não é tocado.</summary>
    public static void PatchWindowedMode(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, (uint)pid);
        if (h == IntPtr.Zero) { Log($"patch windowed: OpenProcess falhou pid={pid} err={Marshal.GetLastWin32Error()}"); return; }
        try
        {
            IntPtr addr = new(WindowedPatchAddr);
            byte[] cur = new byte[1];
            if (!ReadProcessMemory(h, addr, cur, 1, out _)) { Log("patch windowed: ReadProcessMemory falhou"); return; }
            if (cur[0] == WindowedJmpByte) { Log("patch windowed: já aplicado"); return; }
            if (cur[0] != WindowedJzByte) { Log($"patch windowed: byte inesperado 0x{cur[0]:X2} (build diferente) — não toca"); return; }
            if (!VirtualProtectEx(h, addr, 1, PAGE_EXECUTE_READWRITE, out uint old)) return;
            bool ok = WriteProcessMemory(h, addr, new[] { WindowedJmpByte }, 1, out _);
            VirtualProtectEx(h, addr, 1, old, out _);
            Log($"patch windowed: 0x40D46D 0x74 -> 0xEB {(ok ? "OK" : "FALHOU")}");
        }
        finally { CloseHandle(h); }
    }

    /// <summary>O cliente embute keyhook.dll, que instala 2 hooks WH_KEYBOARD_LL que ENGOLEM Alt+Tab/Alt+Esc
    /// /tecla Windows (retornam 1). Os 2 sites de bloqueio são `mov eax,1` (B8 01 00 00 00) nos RVA 0x106D/0x10A2;
    /// o imediato "1" fica em 0x106E/0x10A3. Trocamos por 0 -> o hook retorna 0 e a tecla passa. Patchar a
    /// PROCEDURE (não o install) torna o timing irrelevante. Seguro com o GameGuard morto (offline).</summary>
    public static void PatchKeyHook(string procName)
    {
        uint pid = 0;
        for (int i = 0; i < 240; i++) { pid = ProcessId(procName); if (pid != 0) break; Thread.Sleep(500); }
        if (pid == 0) return;
        IntPtr baseAddr = IntPtr.Zero;
        for (int i = 0; i < 120; i++) { baseAddr = ModuleBase(pid, "keyhook.dll"); if (baseAddr != IntPtr.Zero) break; Thread.Sleep(250); }
        if (baseAddr == IntPtr.Zero) return;

        IntPtr h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
        if (h == IntPtr.Zero) return;
        try { foreach (int rva in new[] { 0x106E, 0x10A3 }) PatchByteToZero(h, baseAddr + rva); }
        finally { CloseHandle(h); }
    }

    private static void PatchByteToZero(IntPtr h, IntPtr addr)
    {
        byte[] cur = new byte[1];
        if (!ReadProcessMemory(h, addr, cur, 1, out _)) return;
        if (cur[0] == 0x00) return;     // já patchado
        if (cur[0] != 0x01) return;     // byte inesperado (build diferente) — não toca
        if (!VirtualProtectEx(h, addr, 1, PAGE_EXECUTE_READWRITE, out uint old)) return;
        WriteProcessMemory(h, addr, new byte[] { 0x00 }, 1, out _);
        VirtualProtectEx(h, addr, 1, old, out _);
    }

    private static IntPtr ModuleBase(uint pid, string name)
    {
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snap == new IntPtr(-1)) return IntPtr.Zero;
        try
        {
            MODULEENTRY32 me = new() { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
            if (!Module32First(snap, ref me)) return IntPtr.Zero;
            do { if (string.Equals(me.szModule, name, StringComparison.OrdinalIgnoreCase)) return me.modBaseAddr; }
            while (Module32Next(snap, ref me));
        }
        finally { CloseHandle(snap); }
        return IntPtr.Zero;
    }
}
