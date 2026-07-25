using System.Diagnostics;

namespace RakionLauncher;

/// <summary>
/// Tela principal — visual do Softnyx Game Launcher (roxo), só com o Rakion: header próprio (drag +
/// minimizar/fechar), banner do Rakion, login (ID/senha) e os botões START GAME / GAME OPTION. O modo
/// de tela e as game options ficam no <see cref="OptionsForm"/> (GAME OPTION); o START GAME grava o INI
/// e lança o jogo + aplica o framing da janela.
/// </summary>
internal sealed partial class MainForm : Form
{
    private const string ServerId = "1A";

    private readonly string _clientDir, _binDir, _iniPath, _modeFile;
    private readonly LauncherConfig _launcherConfig;
    private GameSettings _settings;

    private readonly TextBox _user = new();
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true };
    private readonly Label _userLabel = new() { Text = "Login ID" };
    private readonly Label _passLabel = new() { Text = "Password" };
    private readonly Label _friendsTitle = new();
    private readonly ListBox _onlineFriends = new();
    private readonly ComboBox _accountSwitch = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _login = new() { Text = "LOGIN" };
    private readonly Button _switchAccount = new() { Text = "OUTRA\nCONTA" };
    private readonly Button _play = new() { Text = "INICIAR\nGAME" };
    private readonly Button _options = new() { Text = "GAME\nOPTIONS" };
    private readonly Button _startBot = new() { Text = "INICIAR\nBOT" };
    private readonly Label _status = new();
    private readonly Label _clientStatus = new();
    private readonly Label _serverStatus = new();
    private readonly System.Windows.Forms.Timer _serverStatusTimer = new() { Interval = 10000 };
    private readonly System.Windows.Forms.Timer _clientStatusTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _friendStatusTimer = new() { Interval = 30000 };
    private readonly ServerStatusClient _serverStatusClient = new();
    private readonly OnlineFriendsClient _onlineFriendsClient = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();

    private bool _drag; private Point _dragOrigin;
    private bool _exitRequested, _trayHintShown;
    private int _clients;
    private readonly AuthenticatedAccountStore _accounts = new();
    private bool _updatingAccountSwitch;
    private readonly List<HeadlessBotSession> _headlessBots = [];

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
        BuildTray();
        BuildBanner();
        BuildStatus();          // antes dos botões: o status fica atrás, os botões na frente
        BuildLoginAndButtons();
        ShowLoginState();
        Shown += async (_, _) =>
        {
            RefreshClientCount();
            await RefreshServerStatusAsync();
        };
        _serverStatusTimer.Tick += async (_, _) => await RefreshServerStatusAsync();
        _friendStatusTimer.Tick += async (_, _) => await RefreshOnlineFriendsAsync();
        _clientStatusTimer.Tick += (_, _) => RefreshClientCount();
        _serverStatusTimer.Start();
        _friendStatusTimer.Start();
        _clientStatusTimer.Start();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
            StopHeadlessBots();
            _serverStatusTimer.Dispose();
            _friendStatusTimer.Dispose();
            _clientStatusTimer.Dispose();
            _trayIcon.Dispose();
            _trayMenu.Dispose();
        };
    }

    private void BuildTray()
    {
        var open = new ToolStripMenuItem("Abrir launcher");
        open.Click += (_, _) => RestoreFromTray();
        var exit = new ToolStripMenuItem("Fechar");
        exit.Click += (_, _) => ExitFromTray();
        _trayIcon.Icon = Icon ?? SystemIcons.Application;
        _trayIcon.Text = "Rakion Launcher";
        _trayMenu.Items.Add(open);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exit);
        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left) RestoreFromTray();
        };
        _trayIcon.Visible = true;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_exitRequested || eventArgs.CloseReason != CloseReason.UserClosing) return;
        eventArgs.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        if (_trayHintShown) return;
        _trayHintShown = true;
        _trayIcon.ShowBalloonTip(2500, "Rakion Launcher",
            "O launcher continua aberto na bandeja do sistema.", ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        _trayIcon.Visible = false;
        Close();
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
            Text = "Long awaited totally new game system.\nExtreme strike sensation.",
            AutoSize = false, Bounds = new Rectangle(18, 92, 256, 34), ForeColor = Color.Gainsboro, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9f)
        });
        _serverStatus.Text = "Servidor: verificando…";
        _serverStatus.AutoSize = false;
        _serverStatus.Bounds = new Rectangle(18, 148, 256, 25);
        _serverStatus.ForeColor = Color.Gold;
        _serverStatus.BackColor = Color.Transparent;
        _serverStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        info.Controls.Add(_serverStatus);
        Controls.Add(info);
    }

    private async Task RefreshServerStatusAsync()
    {
        ServerStatusResponse? status = await _serverStatusClient.GetAsync(
            _launcherConfig.UpdateBaseUrl);
        if (IsDisposed) return;
        if (status?.Online != true)
        {
            _serverStatus.Text = "Servidor: Offline";
            _serverStatus.ForeColor = Color.IndianRed;
            return;
        }
        _serverStatus.Text = $"Servidor: Online   ·   Jogadores: {status.OnlinePlayers}/{status.Capacity}";
        _serverStatus.ForeColor = Color.LightGreen;
    }

    private void BuildStatus()
    {
        var box = new Panel { Bounds = new Rectangle(18, 378, 592, 66), BackColor = Theme.Panel, BorderStyle = BorderStyle.FixedSingle };
        _status.Bounds = new Rectangle(10, 6, 410, 54); _status.ForeColor = Theme.Ink; _status.Font = new Font("Segoe UI", 8.5f);
        _status.Text = $"Pronto.   Modo de tela: {ModeLabel(_settings.DisplayMode)}   ·   {_settings.ScreenWidth} x {_settings.ScreenHeight}";
        _clientStatus.Bounds = new Rectangle(424, 6, 158, 54);
        _clientStatus.TextAlign = ContentAlignment.MiddleRight;
        _clientStatus.ForeColor = Theme.Ink;
        _clientStatus.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        box.Controls.Add(_status);
        box.Controls.Add(_clientStatus);
        Controls.Add(box);
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
        _accountSwitch.Enabled = false;
        try
        {
            if (!File.Exists(Path.Combine(_binDir, GameLauncher.GameProcess))) { Status($"rakion.exe não encontrado em {_binDir}", true); return; }
            AuthenticatedAccount? account = _accounts.Active;
            if (account is null)
            {
                Status("Faça login antes de iniciar o jogo.", true);
                return;
            }

            ClientCompatibility.Install(_binDir);
            RefreshClientCount();
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
            ClientCompatibility.ValidateInstalled(_binDir);

            _settings.Save(_iniPath, _modeFile);   // garante o m_bActiveFullScreen certo no INI antes de lançar
            string mode = _settings.DisplayMode;
            // version.dll carrega RakionClientPatch.dll antes do entry point; o launcher mantém o processo
            // suspenso até terminar o bootstrap e então cuida do framing da janela.
            LaunchAuthentication authentication =
                await EnsureAuthenticationAsync(account, clientVersion);
            var (pid, hThread) = GameLauncher.LaunchSuspended(
                _binDir, account.User, GameLauncher.HexPass(authentication.Credential), ServerId);
            WindowMode.Log($"launch cliente: abertos={_clients} user='{account.User}' pid={pid}");
            GameLauncher.Resume(hThread);
            account.Authentication = null;

            uint upid = (uint)pid;   // frama/patcha por PID -> cada cliente cuida da SUA janela (suporta vários)
            int w = _settings.ScreenWidth, h = _settings.ScreenHeight;   // alvo do framing = resolução escolhida
            new Thread(() => WindowMode.FrameGameWindow(upid, mode, w, h)) { IsBackground = true }.Start();
            new Thread(() => WindowMode.InjectDiagDll(pid)) { IsBackground = true }.Start();   // dev-only (opt-in RAKION_DIAG_DLL): hook de RE pós-loader

            RefreshClientCount();
            Status("Rakion iniciado. Pode abrir outro no START GAME.", false);
        }
        catch (Exception ex) { Status(ex.Message, true); }
        finally
        {
            _play.Enabled = true;
            _accountSwitch.Enabled = true;
        }
    }

    private void Status(string msg, bool error) { _status.ForeColor = error ? Color.Firebrick : Theme.Ink; _status.Text = msg; }

    private void RefreshClientCount()
    {
        _clients = GameLauncher.CountRunning(_binDir);
        _clientStatus.Text = $"Clientes abertos: {_clients}";
    }

    private static string ResolveClientDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Bin", GameLauncher.GameProcess))) return dir.FullName;
        var env = Environment.GetEnvironmentVariable("RAKION_DIR");
        return !string.IsNullOrEmpty(env) ? env : AppContext.BaseDirectory;
    }
}
