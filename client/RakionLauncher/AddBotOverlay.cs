using System.Runtime.InteropServices;
using System.Text;

namespace RakionLauncher;

/// <summary>
/// Botão "Adicionar Bot" OVERLAY sobre a janela do jogo. O cliente é um binário fechado e hookar a UI
/// dele (Frida) CONGELA a thread de render; então o botão nativo na tela da sala não é viável de forma
/// confiável. Solução: o launcher (que já acha e reformata a janela "Rakion") desenha um botão topmost
/// por cima, ancorado na barra inferior. O clique foca o jogo e DIGITA "/addbot" no chat via SendInput
/// (Unicode = teclado real, o que o engine lê pro chat) — reusando o comando que já funciona no servidor.
///
/// Roda numa thread STA própria (message pump) e re-posiciona junto da janela do jogo (que o engine
/// recria ao trocar de cena). Some quando o jogo fecha.
/// </summary>
internal static class AddBotOverlay
{
    private const string GameWndClass = "Rakion";

    // ---- user32 ----
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr p);
    [DllImport("user32.dll")] private static extern int EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder buf, int max);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rc);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D;

    private static IntPtr FindGameWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint wpid);
            if (wpid != pid || !IsWindowVisible(hwnd)) return true;
            var sb = new StringBuilder(64); GetClassName(hwnd, sb, sb.Capacity);
            if (sb.ToString() != GameWndClass) return true;
            found = hwnd; return false;
        }, IntPtr.Zero);
        return found;
    }

    private static uint ProcessId(string procName)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(procName)))
            return (uint)p.Id;
        return 0;
    }

    /// <summary>Sobe o overlay numa thread STA. Chamar uma vez depois do Resume do jogo.</summary>
    public static void Start(string procName)
    {
        var t = new Thread(() => Run(procName)) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    private static void Run(string procName)
    {
        var btn = new StoneButton { Text = "Add Bot", Dock = DockStyle.Fill };
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            TopMost = true,
            StartPosition = FormStartPosition.Manual,
            Size = new Size(102, 29),
        };
        form.Controls.Add(btn);
        btn.Click += (_, _) => SendAddBot(procName);

        // Ancora o botão na BARRA INFERIOR do jogo (na lacuna entre os grupos de botões, em linha com
        // Game setting), proporcional à área de cliente — funciona em qualquer resolução. O engine recria
        // a janela ao trocar de cena; re-acha e re-posiciona sempre.
        var timer = new System.Windows.Forms.Timer { Interval = 350 };
        timer.Tick += (_, _) =>
        {
            uint pid = ProcessId(procName);
            if (pid == 0) { form.Close(); return; }
            IntPtr g = FindGameWindow(pid);
            if (g == IntPtr.Zero) { form.Visible = false; return; }
            GetClientRect(g, out RECT rc);
            int cw = rc.Right - rc.Left, ch = rc.Bottom - rc.Top;
            if (cw < 320 || ch < 240) { form.Visible = false; return; }
            var origin = new POINT { X = rc.Left, Y = rc.Top };
            ClientToScreen(g, ref origin);
            int w = Math.Max(86, (int)(cw * 0.0747));   // ~102px a 1366 de largura (igual aos botões do jogo)
            int h = Math.Max(24, (int)(ch * 0.041));     // ~29px
            // lacuna entre o grupo esquerdo (Previous/Messenger/Invite) e o direito (Game setting/Team/Start),
            // na MESMA linha da barra de botões inferior.
            int x = origin.X + (int)(cw * 0.462);
            int y = origin.Y + (int)(ch * 0.947);
            if (form.Size.Width != w || form.Size.Height != h) form.Size = new Size(w, h);
            if (form.Location.X != x || form.Location.Y != y) form.Location = new Point(x, y);
            if (!form.Visible) form.Visible = true;
        };
        timer.Start();
        try { Application.Run(form); } catch { }
    }

    /// <summary>Botão desenhado pra imitar os botões de PEDRA do Rakion: gradiente cinza-pedra, borda
    /// biselada (relevo), texto claro em negrito. Sem textura do jogo (que está num XFS), mas bem próximo.</summary>
    private sealed class StoneButton : Button
    {
        public StoneButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            TabStop = false; UseVisualStyleBackColor = false;
            Font = new Font("Tahoma", 8.25f, FontStyle.Bold);
            ForeColor = Color.FromArgb(232, 226, 214);
        }
        private bool _hover, _down;
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var r = ClientRectangle;
            int top = _down ? 70 : (_hover ? 132 : 116), bot = _down ? 48 : (_hover ? 92 : 80);
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(r,
                Color.FromArgb(top, top - 4, top - 10), Color.FromArgb(bot, bot - 3, bot - 8), 90f))
                g.FillRectangle(br, r);
            // bisel: claro em cima/esquerda, escuro embaixo/direita
            using (var lt = new Pen(Color.FromArgb(_down ? 60 : 158, _down ? 58 : 152, _down ? 54 : 142)))
            { g.DrawLine(lt, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(lt, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var dk = new Pen(Color.FromArgb(46, 44, 40)))
            { g.DrawLine(dk, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(dk, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1); }
            TextRenderer.DrawText(g, Text, Font, r, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Foca o jogo e DIGITA "/addbot" + Enter no chat (SendInput Unicode = teclado real).
    /// Abre o chat com um Enter antes (idempotente: se já estiver no jogo, o Enter abre o input).</summary>
    private static void SendAddBot(string procName)
    {
        uint pid = ProcessId(procName);
        if (pid == 0) return;
        IntPtr g = FindGameWindow(pid);
        if (g == IntPtr.Zero) return;
        SetForegroundWindow(g);
        Thread.Sleep(120);
        TapEnter();                 // abre/foca o chat
        Thread.Sleep(60);
        foreach (char c in "/addbot") TypeUnicode(c);
        Thread.Sleep(40);
        TapEnter();                 // envia
    }

    private static void TypeUnicode(char c)
    {
        var inputs = new[]
        {
            KeyUnicode(c, false),
            KeyUnicode(c, true),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        Thread.Sleep(12);
    }

    private static INPUT KeyUnicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), time = 0, dwExtraInfo = IntPtr.Zero } }
    };

    private static void TapEnter()
    {
        var inputs = new[]
        {
            new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_RETURN } } },
            new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_RETURN, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
