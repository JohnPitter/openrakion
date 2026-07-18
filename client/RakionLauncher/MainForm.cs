using System.Diagnostics;

namespace RakionLauncher;

/// <summary>
/// Tela principal — visual do Softnyx Game Launcher (roxo), só com o Rakion: header próprio (drag +
/// minimizar/fechar), banner do Rakion, login (ID/senha) e os botões START GAME / GAME OPTION. O modo
/// de tela e as game options ficam no <see cref="OptionsForm"/> (GAME OPTION); o START GAME grava o INI
/// e lança o jogo + aplica o framing da janela.
/// </summary>
internal sealed class MainForm : Form
{
    private const string ServerId = "1A";

    private readonly string _clientDir, _binDir, _iniPath, _modeFile;
    private readonly LauncherConfig _launcherConfig;
    private GameSettings _settings;

    private readonly TextBox _user = new();
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true };
    private readonly Button _play = new() { Text = "START\nGAME" };
    private readonly Button _options = new() { Text = "GAME\nOPTION" };
    private readonly Label _status = new();

    private bool _drag; private Point _dragOrigin;
    private int _clients;   // nº de clientes abertos (o patch do mutex permite vários)

    public MainForm()
    {
        _clientDir = ResolveClientDir();
        _binDir = Path.Combine(_clientDir, "Bin");
        _iniPath = Path.Combine(_clientDir, "Scripts", "PersistentSymbols.ini");
        _modeFile = Path.Combine(_clientDir, "display.mode");
        _settings = GameSettings.Load(_iniPath, _modeFile);
        _launcherConfig = LauncherConfig.Load();

        Text = "Rakion Launcher";
        Icon = Theme.LoadIcon("app.ico");
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(628, 456);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Lilac;
        Font = new Font("Segoe UI", 9f);

        BuildHeader();
        BuildBanner();
        BuildStatus();          // antes dos botões: o status fica atrás, os botões na frente
        BuildLoginAndButtons();
    }

    private void BuildHeader()
    {
        var header = new Panel { Bounds = new Rectangle(0, 0, ClientSize.Width, 38), BackColor = Theme.Dark };
        header.Controls.Add(new Label
        {
            Text = "Rakion  —  Game Launcher", AutoSize = false, Bounds = new Rectangle(14, 0, 400, 38),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI", 11f, FontStyle.Bold)
        });
        var close = HeaderButton("✕", ClientSize.Width - 36); close.Click += (_, _) => Close();
        var min = HeaderButton("—", ClientSize.Width - 70); min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        header.Controls.Add(close); header.Controls.Add(min);
        header.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _drag = true; _dragOrigin = e.Location; } };
        header.MouseMove += (_, e) => { if (_drag) Location = new Point(Location.X + e.X - _dragOrigin.X, Location.Y + e.Y - _dragOrigin.Y); };
        header.MouseUp += (_, _) => _drag = false;
        Controls.Add(header);
    }

    private static Label HeaderButton(string glyph, int x)
    {
        var l = new Label { Text = glyph, AutoSize = false, Bounds = new Rectangle(x, 7, 26, 24), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        l.MouseEnter += (_, _) => l.ForeColor = Color.Gold;
        l.MouseLeave += (_, _) => l.ForeColor = Color.White;
        return l;
    }

    private void BuildBanner()
    {
        Controls.Add(new Label { Text = "Select Game", AutoSize = true, Location = new Point(18, 46), ForeColor = Theme.Ink, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        // arte do Rakion (esquerda)
        Controls.Add(new PictureBox
        {
            Bounds = new Rectangle(18, 68, 300, 182), SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Dark, BorderStyle = BorderStyle.FixedSingle, Image = Theme.Load("rakion_banner.png")
        });
        // painel de descrição (direita)
        var info = new Panel { Bounds = new Rectangle(322, 68, 288, 182), BackColor = Theme.Dark, BorderStyle = BorderStyle.FixedSingle };
        info.Controls.Add(new Label { Text = "RAKION", AutoSize = true, Location = new Point(16, 18), ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI", 20f, FontStyle.Bold) });
        info.Controls.Add(new Label { Text = "Chaos Force", AutoSize = true, Location = new Point(20, 58), ForeColor = Color.Gold, BackColor = Color.Transparent, Font = new Font("Segoe UI", 11f, FontStyle.Italic) });
        info.Controls.Add(new Label
        {
            Text = "Long awaited totally new game system.\nYou can not run away from the\nextreme strike sensation.",
            AutoSize = false, Bounds = new Rectangle(18, 96, 256, 78), ForeColor = Color.Gainsboro, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9f)
        });
        Controls.Add(info);
    }

    private void BuildLoginAndButtons()
    {
        Controls.Add(new Label { Text = "Login ID", AutoSize = true, Location = new Point(22, 264), ForeColor = Theme.Ink, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _user.SetBounds(22, 284, 210, 26);
        Controls.Add(new Label { Text = "Password", AutoSize = true, Location = new Point(22, 316), ForeColor = Theme.Ink, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _pass.SetBounds(22, 336, 210, 26);
        Controls.Add(_user); Controls.Add(_pass);

        _play.SetBounds(366, 262, 120, 100); Theme.StyleButton(_play, primary: true); _play.TextAlign = ContentAlignment.MiddleCenter; _play.Click += OnPlay;
        _options.SetBounds(494, 262, 120, 100); Theme.StyleButton(_options); _options.TextAlign = ContentAlignment.MiddleCenter; _options.Click += OnOptions;
        Controls.Add(_play); Controls.Add(_options);
    }

    private void BuildStatus()
    {
        var box = new Panel { Bounds = new Rectangle(18, 378, 592, 66), BackColor = Theme.Panel, BorderStyle = BorderStyle.FixedSingle };
        _status.Bounds = new Rectangle(10, 6, 572, 54); _status.ForeColor = Theme.Ink; _status.Font = new Font("Segoe UI", 8.5f);
        _status.Text = $"Pronto.   Modo de tela: {ModeLabel(_settings.DisplayMode)}   ·   {_settings.ScreenWidth} x {_settings.ScreenHeight}";
        box.Controls.Add(_status); Controls.Add(box);
    }

    private static string ModeLabel(string m) => m switch
    {
        WindowMode.Windowed => "Janela", WindowMode.Borderless => "Janela sem borda", _ => "Tela cheia"
    };

    private void OnOptions(object? sender, EventArgs e)
    {
        using var dlg = new OptionsForm(_settings, _iniPath, _modeFile);
        dlg.ShowDialog(this);                 // o OK/Aplicar do diálogo já gravam o INI
        _settings = dlg.Result;
        Status($"Modo de tela: {ModeLabel(_settings.DisplayMode)}   ·   {_settings.ScreenWidth} x {_settings.ScreenHeight}", false);
    }

    private async void OnPlay(object? sender, EventArgs e)
    {
        _play.Enabled = false;
        try
        {
            if (!File.Exists(Path.Combine(_binDir, GameLauncher.GameProcess))) { Status($"rakion.exe não encontrado em {_binDir}", true); return; }
            if (_user.Text.Trim() == "") { Status("informe o usuário", true); return; }
            if (_pass.Text == "") { Status("informe a senha", true); return; }

            int clientVersion = UpdateClient.GetInstalledVersion(
                _clientDir, _launcherConfig.BaseVersion);
            if (_clients == 0 && _launcherConfig.UpdatesEnabled)
            {
                var updater = new UpdateClient();
                var progress = new Progress<string>(message => Status(message, false));
                clientVersion = await updater.ApplyLatestAsync(
                    _clientDir, _launcherConfig, progress);
                Status($"Cliente validado na versão {clientVersion}.", false);
                if (!File.Exists(Path.Combine(_binDir, GameLauncher.GameProcess)))
                    throw new FileNotFoundException("O update não contém rakion.exe.");
            }

            _settings.Save(_iniPath, _modeFile);   // garante o m_bActiveFullScreen certo no INI antes de lançar
            ClientCompatibility.Install(_binDir);
            string mode = _settings.DisplayMode;
            // A DLL proxy version.dll aplica os patches antes do entry point; o launcher mantém o processo
            // suspenso apenas até terminar o bootstrap e então cuida do framing da janela.
            string user = _user.Text.Trim();
            string credential = await new LaunchAuthenticator().GetCredentialAsync(
                _launcherConfig, clientVersion, user, _pass.Text);
            var (pid, hThread) = GameLauncher.LaunchSuspended(
                _binDir, user, GameLauncher.HexPass(credential), ServerId);
            WindowMode.Log($"launch cliente #{_clients + 1}: user='{user}' pid={pid}");   // diagnóstico: que conta foi lançada
            GameLauncher.Resume(hThread);

            uint upid = (uint)pid;   // frama/patcha por PID -> cada cliente cuida da SUA janela (suporta vários)
            int w = _settings.ScreenWidth, h = _settings.ScreenHeight;   // alvo do framing = resolução escolhida
            new Thread(() => WindowMode.FrameGameWindow(upid, mode, w, h)) { IsBackground = true }.Start();
            new Thread(() => WindowMode.InjectDiagDll(pid)) { IsBackground = true }.Start();   // dev-only (opt-in RAKION_DIAG_DLL): hook de RE pós-loader

            _clients++;
            Status($"Rakion iniciado — {_clients} cliente(s) aberto(s). Pode abrir outro no START GAME.", false);
        }
        catch (Exception ex) { Status(ex.Message, true); }
        finally { _play.Enabled = true; }
    }

    private void Status(string msg, bool error) { _status.ForeColor = error ? Color.Firebrick : Theme.Ink; _status.Text = msg; }

    private static string ResolveClientDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Bin", GameLauncher.GameProcess))) return dir.FullName;
        var env = Environment.GetEnvironmentVariable("RAKION_DIR");
        return !string.IsNullOrEmpty(env) ? env : AppContext.BaseDirectory;
    }
}
