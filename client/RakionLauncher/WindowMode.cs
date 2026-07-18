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
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hwnd, string s);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int idx);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    private static readonly IntPtr DpiUnaware = new(-1);   // DPI_AWARENESS_CONTEXT_UNAWARE

    // Log de diagnóstico do framing (integração com o cliente) — %TEMP%\rakion_launcher.log.
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "rakion_launcher.log");
    internal static void Log(string msg) { try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}"); } catch { } }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPLACEMENT { public uint length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }

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

    internal static bool IsAlive(uint pid)
    {
        try { using var p = Process.GetProcessById((int)pid); return !p.HasExited; } catch { return false; }
    }

    private static IntPtr WaitForGameWindow(uint pid, int tries)
    {
        for (int i = 0; i < tries && IsAlive(pid); i++)
        {
            var h = FindGameWindow(pid);
            if (h != IntPtr.Zero) return h;
            Thread.Sleep(500);
        }
        return IntPtr.Zero;
    }

    /// <summary>Espera a janela de render e a reformata pro modo dado. O client é fixado na resolução
    /// ESCOLHIDA (<paramref name="wantW"/>×<paramref name="wantH"/> = o que está no INI = backbuffer do
    /// engine), não no tamanho intermediário que o init cria. Frama UMA vez, depois do engine estabilizar,
    /// e só re-frama se o engine regredir o estilo — sem brigar por posição (era o que fazia a tela piscar).
    /// No-op em fullscreen (exclusivo não precisa de framing).</summary>
    public static void FrameGameWindow(uint pid, string mode, int wantW, int wantH)
    {
        if (mode == Fullscreen) return;
        bool fill = mode == Borderless;
        // Mede/reformata no espaço DPI-unaware do jogo (Serious Engine é DPI-unaware) -> as coordenadas
        // e métricas de tela batem; sem isso, em telas HiDPI a janela fica deslocada/cortada.
        SetThreadDpiAwarenessContext(DpiUnaware);
        new Thread(() => KeepWindowAccessible(pid)) { IsBackground = true }.Start();

        if (WaitForGameWindow(pid, 180) == IntPtr.Zero) return;   // ~90s (MD5/GameGuard antes da janela)

        int cw = 0, ch = 0;
        IntPtr framed = IntPtr.Zero;
        // Roda a SESSÃO INTEIRA: o engine RECRIA a janela de render ao trocar de cena (login -> char select ->
        // sala) e ao reinicializar o display; com um hwnd fixo a gente fica preso no handle morto (vira 0x0).
        // Então re-acha a janela "Rakion" DESTE pid sempre e re-frama a recriada (por pid -> suporta N clientes).
        bool wasIconic = false;
        while (IsAlive(pid))
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
                    if (!wasIconic) StripFrame(hwnd, cw, ch);
                }
                else if (hwnd != framed)   // primeira janela ou recriada: espera estabilizar e frama do zero
                {
                    var (engW, engH) = WaitClientStable(hwnd, pid);
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

    /// <summary>Tira o título (vira popup) enquanto a janela está minimizada pra que, ao restaurar, o engine
    /// veja window==client==backbuffer e não encolha. E define o RECT de restauração no tamanho ALVO (popup):
    /// assim o engine reseta o backbuffer já no tamanho final e o re-add do título não muda o client — corta
    /// o 2º device-reset (menos tela preta). Sem SetWindowPos no estilo: a janela está invisível.</summary>
    private static void StripFrame(IntPtr hwnd, int clientW, int clientH)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        if ((style & WS_CAPTION) != 0)
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr((style & ~TitledStyle) | WS_POPUP));
        if (clientW < 320 || clientH < 240) return;
        WINDOWPLACEMENT wp = new() { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref wp)) return;
        var (sw, sh) = ScreenSize();
        int x = Math.Max((sw - clientW) / 2, 0), y = Math.Max((sh - clientH) / 2, 0);
        wp.rcNormalPosition = new RECT { Left = x, Top = y, Right = x + clientW, Bottom = y + clientH };
        SetWindowPlacement(hwnd, ref wp);
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
    private static (int w, int h) WaitClientStable(IntPtr hwnd, uint pid)
    {
        int lastW = -1, lastH = -1, stable = 0;
        for (int i = 0; i < 40 && IsAlive(pid) && IsWindow(hwnd); i++)   // até ~10s; aborta se a janela morre
        {
            var (w, h) = ClientSize(hwnd);
            if (w >= 320 && h >= 240 && w == lastW && h == lastH) { if (++stable >= 3) return (w, h); }
            else stable = 0;
            lastW = w; lastH = h;
            Thread.Sleep(250);
        }
        return (lastW >= 320 ? lastW : 800, lastH >= 240 ? lastH : 600);
    }

    // non-client real (borda+título) aprendido de um frame bom — deixa o ApplyTitledFrame acertar o tamanho da
    // janela DE PRIMEIRA, sem o passo intermediário do AdjustWindowRect (que erra a borda DWM e mexe no client,
    // forçando um device-reset a mais ao restaurar de minimizado).
    private static int _ncW = -1, _ncH = -1;

    private static void ApplyTitledFrame(IntPtr hwnd, int clientW, int clientH, bool activate, bool fill)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        style &= ~WS_POPUP;
        style |= TitledStyle;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
        ClearTopmost(hwnd);
        SetWindowText(hwnd, "Rakion");

        if (_ncW >= 0)   // non-client conhecido: acerta o tamanho de 1ª (não passa por um client intermediário)
        {
            PlaceWindow(hwnd, clientW + _ncW, clientH + _ncH, activate, fill);
        }
        else             // 1ª vez: estima via AdjustWindowRect e corrige o gap residual da borda DWM/tema
        {
            RECT rc = new() { Left = 0, Top = 0, Right = clientW, Bottom = clientH };
            AdjustWindowRect(ref rc, (uint)(style & 0xFFFFFFFF), false);
            int winW = rc.Right - rc.Left, winH = rc.Bottom - rc.Top;
            PlaceWindow(hwnd, winW, winH, activate, fill);
            var (acw, ach) = ClientSize(hwnd);
            if (acw > 0 && ach > 0 && (acw != clientW || ach != clientH))
                PlaceWindow(hwnd, winW + (clientW - acw), winH + (clientH - ach), activate, fill);
        }
        LearnNonClient(hwnd, clientW, clientH);   // aprende/atualiza o non-client real quando o client bateu o alvo
        if (activate) FocusWindow(hwnd);
    }

    private static void LearnNonClient(IntPtr hwnd, int clientW, int clientH)
    {
        var (cw, ch) = ClientSize(hwnd);
        if (cw <= 0 || ch <= 0 || Math.Abs(cw - clientW) > 2 || Math.Abs(ch - clientH) > 2) return;
        GetWindowRect(hwnd, out RECT wr);
        int ncW = (wr.Right - wr.Left) - cw, ncH = (wr.Bottom - wr.Top) - ch;
        if (ncW is >= 0 and < 60 && ncH is >= 0 and < 120) { _ncW = ncW; _ncH = ncH; }
    }

    private static void PlaceWindow(IntPtr hwnd, int winW, int winH, bool activate, bool fill)
    {
        int x = 0, y = 0;
        if (!fill) { var (sw, sh) = ScreenSize(); x = Math.Max((sw - winW) / 2, 0); y = Math.Max((sh - winH) / 2, 0); }
        uint flags = SWP_FRAMECHANGE | SWP_NOZORDER | SWP_SHOWWINDOW;
        if (!activate) flags |= SWP_NOACTIVATE;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, winW, winH, flags);
    }

    private static void ClearTopmost(IntPtr hwnd)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_TOPMOST) != 0)
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static void FocusWindow(IntPtr hwnd) { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }

    /// <summary>Roda a sessão toda só impedindo que o jogo fique always-on-top (pro Alt+Tab raisar outras
    /// janelas). A trava real do Alt+Tab é removida pela DLL de compatibilidade.</summary>
    private static void KeepWindowAccessible(uint pid)
    {
        while (IsAlive(pid))   // re-acha a janela deste pid (o engine recria ao trocar de cena)
        {
            IntPtr hwnd = FindGameWindow(pid);
            if (hwnd != IntPtr.Zero) ClearTopmost(hwnd);
            Thread.Sleep(500);
        }
    }

    // ---- helpers de processo ----
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern bool Module32First(IntPtr snap, ref MODULEENTRY32 me);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern bool Module32Next(IntPtr snap, ref MODULEENTRY32 me);

    private const uint PROCESS_VM_OPERATION = 0x0008, PROCESS_VM_READ = 0x0010, PROCESS_VM_WRITE = 0x0020;
    private const uint TH32CS_SNAPMODULE = 0x08, TH32CS_SNAPMODULE32 = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MODULEENTRY32
    {
        public uint dwSize, th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
        public IntPtr modBaseAddr; public uint modBaseSize; public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    // ---- injeção de DLL de DIAGNÓSTICO (dev-only, RE de interoperabilidade) ----
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr mod, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, uint size, uint type, uint prot);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr sa, uint stack, IntPtr start, IntPtr param, uint flags, out uint tid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeThread(IntPtr h, out uint code);

    private const uint PROCESS_CREATE_THREAD = 0x0002, PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, PAGE_READWRITE = 0x04;

    /// <summary>
    /// Injeta uma DLL de DIAGNÓSTICO no jogo via LoadLibraryA num remote thread. Chamado DEPOIS do Resume,
    /// numa thread de fundo: ESPERA a engine.dll aparecer no alvo (= loader do Windows inicializado — injetar
    /// no processo ainda SUSPENSO falha, o LdrInitializeThunk não rodou), então injeta e VERIFICA o carregamento
    /// pelo exit-code (HMODULE) + presença do módulo. Opt-in por env var RAKION_DIAG_DLL; sem ela não faz nada
    /// (produção intocada). Só p/ RE do próprio autor (cravar o trigger do AddRemotePlayer). docs headless-engine-re §22.7.
    /// </summary>
    public static void InjectDiagDll(int pid)
    {
        string? dll = Environment.GetEnvironmentVariable("RAKION_DIAG_DLL");
        if (string.IsNullOrWhiteSpace(dll)) return;
        if (!File.Exists(dll)) { Log($"diag-inject: DLL não encontrada: {dll}"); return; }

        // espera o loader do alvo subir (engine.dll mapeada) — sinal de que o LoadLibrary remoto vai funcionar
        for (int i = 0; i < 120 && IsAlive((uint)pid); i++) { if (ModuleBase((uint)pid, "engine.dll") != IntPtr.Zero) break; Thread.Sleep(100); }
        if (ModuleBase((uint)pid, "engine.dll") == IntPtr.Zero) { Log("diag-inject: engine.dll não subiu no alvo — aborta"); return; }
        Thread.Sleep(300);   // folga p/ o loader assentar antes do LoadLibrary remoto

        IntPtr h = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) { Log($"diag-inject: OpenProcess falhou err={Marshal.GetLastWin32Error()}"); return; }
        try
        {
            byte[] path = System.Text.Encoding.ASCII.GetBytes(dll + "\0");
            IntPtr rem = VirtualAllocEx(h, IntPtr.Zero, (uint)path.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (rem == IntPtr.Zero) { Log($"diag-inject: VirtualAllocEx falhou err={Marshal.GetLastWin32Error()}"); return; }
            if (!WriteProcessMemory(h, rem, path, path.Length, out _)) { Log("diag-inject: WriteProcessMemory falhou"); return; }
            IntPtr loadLib = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
            IntPtr t = CreateRemoteThread(h, IntPtr.Zero, 0, loadLib, rem, 0, out _);
            if (t == IntPtr.Zero) { Log($"diag-inject: CreateRemoteThread falhou err={Marshal.GetLastWin32Error()} (anti-tamper?)"); return; }
            WaitForSingleObject(t, 8000);
            GetExitCodeThread(t, out uint hmod);   // = HMODULE da DLL (LoadLibraryA), 0 = FALHOU o load
            CloseHandle(t);
            string name = Path.GetFileName(dll);
            IntPtr modBase = ModuleBase((uint)pid, name);
            if (hmod == 0 && modBase == IntPtr.Zero) Log($"diag-inject: LoadLibrary FALHOU (hmod=0, módulo ausente) — {dll}");
            else Log($"diag-inject: OK -> {name} carregada (hmod=0x{hmod:X8}, base={modBase:X}) em pid={pid}");
        }
        finally { CloseHandle(h); }
    }

    internal static IntPtr ModuleBase(uint pid, string name)
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
