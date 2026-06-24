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
    private static void Log(string msg) { try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}"); } catch { } }

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

    private static IntPtr WaitForGameWindow(int pid, int tries)
    {
        for (int i = 0; i < tries; i++)
        {
            if (GameLauncher.IsAlive(pid)) { var h = FindGameWindow((uint)pid); if (h != IntPtr.Zero) return h; }
            Thread.Sleep(500);
        }
        return IntPtr.Zero;
    }

    /// <summary>Espera a janela de render e a reformata pro modo dado. O client é fixado na resolução
    /// ESCOLHIDA (<paramref name="wantW"/>×<paramref name="wantH"/> = o que está no INI = backbuffer do
    /// engine), não no tamanho intermediário que o init cria. Frama UMA vez, depois do engine estabilizar,
    /// e só re-frama se o engine regredir o estilo — sem brigar por posição (era o que fazia a tela piscar).
    /// No-op em fullscreen (exclusivo não precisa de framing).</summary>
    public static void FrameGameWindow(int pid, string mode, int wantW, int wantH)
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
        // Então re-acha a janela "Rakion" do NOSSO pid sempre e re-frama a recriada. Por PID (não por nome): com
        // 2 clientes na mesma máquina cada thread de framing cuida só da janela do seu processo. Janela
        // minimizada pelo usuário: respeita.
        bool wasIconic = false;
        while (GameLauncher.IsAlive(pid))
        {
            IntPtr hwnd = FindGameWindow((uint)pid);
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
                else                       // mesma janela visível: re-frama SÓ se o engine regrediu estilo/tamanho.
                {                          // NÃO re-framar por POSIÇÃO — senão o loop puxava a janela de volta ao
                                           // centro a cada 300ms e o usuário não conseguia arrastá-la (windowed).
                    long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                    var (acw, ach) = ClientSize(hwnd);
                    bool lost = (style & WS_CAPTION) == 0;   // restaurou como popup (strippado) -> re-titula
                    bool wrongSize = Math.Abs(acw - cw) > 8 || Math.Abs(ach - ch) > 8;
                    if (lost || wrongSize)
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
        // Preserva a posição onde o usuário deixou a janela (rcNormalPosition atual); só fixa o TAMANHO alvo no
        // rect de restauração (p/ o engine resetar o backbuffer no tamanho final = corta o 2º device-reset).
        // Antes centralizava — o restore de minimizado jogava a janela de volta ao centro.
        int x = wp.rcNormalPosition.Left, y = wp.rcNormalPosition.Top;
        wp.rcNormalPosition = new RECT { Left = x, Top = y, Right = x + clientW, Bottom = y + clientH };
        SetWindowPlacement(hwnd, ref wp);
    }

    private static string FmtClient(IntPtr h) { var (w, c) = ClientSize(h); return $"{w}x{c}"; }
    private static string FmtRect(IntPtr h) { GetWindowRect(h, out RECT r); return $"{r.Left},{r.Top}-{r.Right},{r.Bottom}"; }

    /// <summary>Espera o client da janela ficar estável (mesmo tamanho por 3 leituras) — sinal de que o
    /// engine terminou de criar/redimensionar o display. Devolve esse tamanho (fallback se nunca estabilizar).</summary>
    private static (int w, int h) WaitClientStable(IntPtr hwnd, int pid)
    {
        int lastW = -1, lastH = -1, stable = 0;
        for (int i = 0; i < 40 && GameLauncher.IsAlive(pid) && IsWindow(hwnd); i++)   // até ~10s; aborta se a janela morre
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
        uint flags = SWP_FRAMECHANGE | SWP_NOZORDER | SWP_SHOWWINDOW;
        if (!activate) flags |= SWP_NOACTIVATE;
        int x = 0, y = 0;
        // borderless cola no canto (0,0); windowed centraliza só no 1º frame (activate=true). Num RE-frame de
        // janela windowed (activate=false) PRESERVA a posição atual (SWP_NOMOVE) — é o que deixa o usuário
        // ARRASTAR a janela: sem isso o framing a reposicionava no centro a cada ciclo.
        if (fill) { /* canto (0,0) */ }
        else if (activate) { var (sw, sh) = ScreenSize(); x = Math.Max((sw - winW) / 2, 0); y = Math.Max((sh - winH) / 2, 0); }
        else flags |= SWP_NOMOVE;
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
    /// janelas). A trava real do Alt+Tab é removida por <see cref="PatchKeyHook"/>.</summary>
    private static void KeepWindowAccessible(int pid)
    {
        while (GameLauncher.IsAlive(pid))   // re-acha a janela do nosso pid (o engine recria ao trocar de cena)
        {
            IntPtr hwnd = FindGameWindow((uint)pid);
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

    // Patches no rakion.exe (sem ASLR, ImageBase 0x400000). Aplicados com o jogo SUSPENSO. Cada patch é
    // (endereço, byteEsperado, byteNovo); o aplicador valida o byte original (build diferente / já aplicado
    // não é tocado, só logado). Patcher central: PatchByteAt (mesmo caminho usado pelo keyhook).
    //
    // MODO JANELA — 0x40D46D: em [85 C0 (TEST EAX,EAX) / 74 52 (JZ) / FF15.. (CALL [0x4D0468])] o CALL é o setup
    // de fullscreen + troca de resolução do desktop; o JMP (0x74->0xEB) pula ele -> windowed real. (Mesmo patch
    // que o "Window Mode" do NyxLauncher faz; ver RE do RakionLauncher.Loader.)
    private const int WindowedPatchAddr = 0x40D46D;
    //
    // SEM RESET DE DISPLAY AO MINIMIZAR — no WndProc (0x40DB10, classe "Rakion") o engine re-inicializa o display
    // ao restaurar de minimizado (tela preta de ~4s: recria o device D3D9). Em WINDOWED o device não é perdido,
    // então é desnecessário. Os 3 JZ guardam: 0x40DBC2 (SC_RESTORE -> FUN_0040d7b0 "Start new display mode"),
    // 0x40DC1E (WM_ACTIVATEAPP ativar -> FUN_0040bc10 resume), 0x40DC4F (WM_ACTIVATEAPP desativar -> pause).
    // Forçando os 3 JMP, o engine ignora a minimização no nível de display -> sem pause/resume/restart -> sem
    // tela preta. Os 3 juntos: pular só o resume deixaria o render pausado pra sempre. Descoberto por RE (Ghidra)
    // do WndProc do rakion.bin.
    private static readonly int[] NoDisplayResetAddrs = { 0x40DBC2, 0x40DC1E, 0x40DC4F };
    //
    // MULTI-INSTÂNCIA — FUN_00402c80 é o check de instância única: CreateMutexA + (GetLastError() ==
    // ERROR_ALREADY_EXISTS) -> retorna 1 quando já há um cliente aberto. O chamador (0x4171ca) faz
    // `test eax,eax / jne abort`. O imediato 0xB7 (=183=ERROR_ALREADY_EXISTS) do `cmp eax,0xB7` está em
    // 0x402C96; trocando-o por 0xFF, o `sete al` nunca dá 1 (GetLastError aqui só vale 0 ou 0xB7) -> a função
    // sempre devolve 0 -> "nenhuma instância anterior" -> N clientes na mesma máquina. Cravado no disasm.
    private const int MutexCmpImmAddr = 0x402C96;

    private const byte JzByte = 0x74, JmpByte = 0xEB;
    private const byte MutexErrLo = 0xB7, MutexErrNeutralized = 0xFF;

    /// <summary>Patch do modo janela (pula o setup de fullscreen/troca-de-resolução). Jogo deve estar suspenso.</summary>
    public static void PatchWindowedMode(int pid) => ApplyPatches(pid, "windowed", (WindowedPatchAddr, JzByte, JmpByte));

    /// <summary>Patch que impede o engine de re-inicializar o display ao restaurar de minimizado (corta a tela
    /// preta). Só faz sentido em windowed/borderless (device não é perdido). Jogo deve estar suspenso.</summary>
    public static void PatchNoDisplayReset(int pid)
        => ApplyPatches(pid, "no-reset-display", Array.ConvertAll(NoDisplayResetAddrs, a => (a, JzByte, JmpByte)));

    /// <summary>Patch de multi-instância: neutraliza o check de instância única (0x402C80) trocando o imediato
    /// ERROR_ALREADY_EXISTS por um valor impossível -> o check sempre diz "1ª instância" -> N clientes na mesma
    /// máquina. Independe do modo de tela. Jogo deve estar suspenso.</summary>
    public static void PatchMultiInstance(int pid)
        => ApplyPatches(pid, "multi-instance", (MutexCmpImmAddr, MutexErrLo, MutexErrNeutralized));

    /// <summary>Aplica N patches de 1 byte num processo (abre o handle uma vez), validando cada byte original.</summary>
    private static void ApplyPatches(int pid, string tag, params (int Addr, byte Expected, byte New)[] patches)
    {
        IntPtr h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, (uint)pid);
        if (h == IntPtr.Zero) { Log($"patch {tag}: OpenProcess falhou pid={pid} err={Marshal.GetLastWin32Error()}"); return; }
        try { foreach (var p in patches) PatchByteAt(h, new IntPtr(p.Addr), p.Expected, p.New, tag); }
        finally { CloseHandle(h); }
    }

    /// <summary>Troca 1 byte num endereço do processo, validando o byte original (build diferente / já aplicado
    /// não é tocado, só logado). Restaura a proteção de página depois.</summary>
    private static void PatchByteAt(IntPtr h, IntPtr addr, byte expected, byte newVal, string tag)
    {
        long va = addr.ToInt64();
        byte[] cur = new byte[1];
        if (!ReadProcessMemory(h, addr, cur, 1, out _)) { Log($"patch {tag}: read falhou @0x{va:X}"); return; }
        if (cur[0] == newVal) { Log($"patch {tag}: 0x{va:X} já aplicado"); return; }
        if (cur[0] != expected) { Log($"patch {tag}: 0x{va:X} byte inesperado 0x{cur[0]:X2} (esp 0x{expected:X2}) — não toca"); return; }
        if (!VirtualProtectEx(h, addr, 1, PAGE_EXECUTE_READWRITE, out uint old)) return;
        bool ok = WriteProcessMemory(h, addr, new[] { newVal }, 1, out _);
        VirtualProtectEx(h, addr, 1, old, out _);
        Log($"patch {tag}: 0x{va:X} 0x{expected:X2} -> 0x{newVal:X2} {(ok ? "OK" : "FALHOU")}");
    }

    /// <summary>O cliente embute keyhook.dll, que instala 2 hooks WH_KEYBOARD_LL que ENGOLEM Alt+Tab/Alt+Esc
    /// /tecla Windows (retornam 1). Os 2 sites de bloqueio são `mov eax,1` (B8 01 00 00 00) nos RVA 0x106D/0x10A2;
    /// o imediato "1" fica em 0x106E/0x10A3. Trocamos por 0 -> o hook retorna 0 e a tecla passa. Patchar a
    /// PROCEDURE (não o install) torna o timing irrelevante. Seguro com o GameGuard morto (offline).</summary>
    public static void PatchKeyHook(int pid)
    {
        IntPtr baseAddr = IntPtr.Zero;
        for (int i = 0; i < 120; i++) { baseAddr = ModuleBase((uint)pid, "keyhook.dll"); if (baseAddr != IntPtr.Zero) break; Thread.Sleep(250); }
        if (baseAddr == IntPtr.Zero) return;

        IntPtr h = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, (uint)pid);
        if (h == IntPtr.Zero) return;
        try { foreach (int rva in new[] { 0x106E, 0x10A3 }) PatchByteAt(h, baseAddr + rva, 0x01, 0x00, "keyhook"); }
        finally { CloseHandle(h); }
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
