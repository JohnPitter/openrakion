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
    private GameSettings _settings;

    private readonly TextBox _user = new() { Text = "test" };
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true, Text = "test" };
    private readonly Button _play = new() { Text = "START\nGAME" };
    private readonly Button _options = new() { Text = "GAME\nOPTION" };
    private readonly Label _status = new();

    private bool _drag; private Point _dragOrigin;

    public MainForm()
    {
        _clientDir = ResolveClientDir();
        _binDir = Path.Combine(_clientDir, "Bin");
        _iniPath = Path.Combine(_clientDir, "Scripts", "PersistentSymbols.ini");
        _modeFile = Path.Combine(_clientDir, "display.mode");
        _settings = GameSettings.Load(_iniPath, _modeFile);

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

        var register = new LinkLabel
        {
            Text = "Criar conta", AutoSize = true, Location = new Point(246, 340),
            LinkColor = Theme.Dark, ActiveLinkColor = Color.Gold, LinkBehavior = LinkBehavior.HoverUnderline,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        register.LinkClicked += (_, _) => OnRegister();
        Controls.Add(register);

        _play.SetBounds(366, 262, 120, 100); Theme.StyleButton(_play, primary: true); _play.TextAlign = ContentAlignment.MiddleCenter; _play.Click += OnPlay;
        _options.SetBounds(494, 262, 120, 100); Theme.StyleButton(_options); _options.TextAlign = ContentAlignment.MiddleCenter; _options.Click += OnOptions;
        Controls.Add(_play); Controls.Add(_options);
    }

    private void OnRegister()
    {
        using var dlg = new RegisterForm(ServerConfig.BaseUrl(_clientDir));
        dlg.ShowDialog(this);
        if (dlg.CreatedId != null)
        {
            _user.Text = dlg.CreatedId;
            _pass.Focus();
            Status($"Conta '{dlg.CreatedId}' criada. Informe a senha e clique START.", false);
        }
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
        try
        {
            if (!File.Exists(Path.Combine(_binDir, GameLauncher.GameProcess))) { Status($"rakion.exe não encontrado em {_binDir}", true); return; }
            string user = _user.Text.Trim();
            if (user == "") { Status("informe o usuário", true); return; }

            // Valida as credenciais no auth web ANTES de lançar (aviso de login inválido). A senha em hex é a
            // mesma usada no argv do jogo — calcula uma vez e reusa no launch.
            string hexPass = GameLauncher.HexPass(_pass.Text);
            // Bypass do pré-voo p/ CONTROLE de interop contra o worldserv ORIGINAL (docker): o :80 do original é o
            // auth PHP do jogo, sem a rota /launcherlogin. Com o marcador C:\temp\launcher_nologin.txt presente,
            // pula a validação e lança (o rakion.exe faz o próprio login no :40708). No-op sem o marcador.
            if (!File.Exists(@"C:\temp\launcher_nologin.txt"))
            {
                _play.Enabled = false;
                Status("Validando login…", false);
                var login = await AuthClient.LoginAsync(ServerConfig.BaseUrl(_clientDir), user, hexPass);
                _play.Enabled = true;
                if (login == AuthClient.LoginResult.Invalid) { Status("Login ID ou senha inválidos.", true); return; }
                if (login == AuthClient.LoginResult.Unreachable)
                { Status($"Servidor de login indisponível ({ServerConfig.AuthHost(_clientDir)}). O servidor está no ar?", true); return; }
            }

            _settings.Save(_iniPath, _modeFile);   // garante o m_bActiveFullScreen certo no INI antes de lançar
            string mode = _settings.DisplayMode;
            // Lança SUSPENSO, aplica os patches ANTES de o engine inicializar (mutex p/ multi-instância; modo
            // janela ANTES de trocar a resolução do desktop), e só então resume.
            var (pid, hThread) = GameLauncher.LaunchSuspended(_binDir, user, hexPass, ServerId);
            WindowMode.PatchMultiInstance(pid);         // libera N clientes na mesma máquina (sempre, mesmo em fullscreen)
            if (mode != WindowMode.Fullscreen)
            {
                WindowMode.PatchWindowedMode(pid);      // windowed real (não troca a resolução do desktop)
                WindowMode.PatchNoDisplayReset(pid);    // não re-inicializa o display ao restaurar de minimizado
            }
            GameLauncher.Resume(hThread);

            int w = _settings.ScreenWidth, h = _settings.ScreenHeight;   // alvo do framing = resolução escolhida
            new Thread(() => WindowMode.FrameGameWindow(pid, mode, w, h)) { IsBackground = true }.Start();
            new Thread(() => WindowMode.PatchKeyHook(pid)) { IsBackground = true }.Start();

            // Captura (RE do runtime de jogo): se C:\temp\capture_hook.dll existir, injeta CEDO (parent, sem UAC,
            // antes do anti-tamper armar). DLL 100% passivo (só lê memória) -> não trava o cliente. No-op se ausente.
            const string capDll = @"C:\temp\capture_hook.dll";
            if (File.Exists(capDll))
                new Thread(() => {
                    Thread.Sleep(900);
                    string r = GameLauncher.InjectDll(pid, capDll);
                    try { File.WriteAllText(@"C:\temp\cap_inject.txt", $"pid={pid} inject={r}\n"); } catch { }
                }) { IsBackground = true }.Start();

            // HIT×N nativo do bot: injeta bot_reliable.dll — abre o CANAL RELIABLE do slot do bot na memória do
            // cliente (seta player[slot]+0x1d8=1, lendo os slots de C:\temp\bot_slots.txt que o servidor publica).
            // Com o canal aberto, o create-com-colisão que o servidor emite passa pelo caminho NATIVO → o bot vira
            // entidade acertável → o contador HIT×N dispara pelo código do jogo. NÃO dirige posição (o movimento é
            // 100% server-side via 0x30a) — sem clipping. Só engine.dll (offsets estáveis). Ver docs/pvp-stage-re.md
            // §12. Mesma injeção cross-arch/CEDO do capture (a via do launcher; injetar por fora TRAVA o jogo).
            // NÃO coexiste com bot_render.dll (ambos hookam SetAction @0x36102fa0 → duplo-hook): esta é a golden.
            const string botDll = @"C:\temp\bot_reliable.dll";
            if (File.Exists(botDll))
                new Thread(() => {
                    Thread.Sleep(1500);
                    string r = GameLauncher.InjectDll(pid, botDll);
                    try { File.WriteAllText(@"C:\temp\bot_inject.txt", $"pid={pid} inject={r}\n"); } catch { }
                }) { IsBackground = true }.Start();

            // Scanner de diagnóstico (SÓ LEITURA): dumpa os campos do blob do 0x307 (docs/cell-monster-re.md
            // §2.3c) da memória REAL de um NPC nativo (ex.: o golem objetivo do Golem War) — fecha os campos
            // ambíguos que a RE estática não confirmou. Fica ocioso até C:\temp\scan_trigger.txt aparecer
            // (criar já dentro do stage, olhando o NPC) — injetar num processo já rodando via ferramenta externa
            // trava o jogo; aqui é a mesma via segura do bot_render.dll. No-op se ausente.
            const string scanDll = @"C:\temp\npc_scan.dll";
            if (File.Exists(scanDll))
                new Thread(() => {
                    Thread.Sleep(1500);
                    string r = GameLauncher.InjectDll(pid, scanDll);
                    try { File.WriteAllText(@"C:\temp\npc_scan_inject.txt", $"pid={pid} inject={r}\n"); } catch { }
                }) { IsBackground = true }.Start();

            // CONTROLE de host-election (observador, SÓ LEITURA): se C:\temp\sessprobe.dll existir, injeta CEDO e
            // loga se ESTE cliente chama StartPeerToPeer_t (hospeda, role=ff) ou JoinSession_t (entra) no stage-entry
            // -> C:\temp\sessprobe_<pid>.log. Prova contra o worldserv ORIGINAL se o MASTER hospeda. No-op se ausente.
            const string sessDll = @"C:\temp\sessprobe.dll";
            if (File.Exists(sessDll))
                new Thread(() => {
                    Thread.Sleep(1000);
                    string r = GameLauncher.InjectDll(pid, sessDll);
                    try { File.WriteAllText(@"C:\temp\sessprobe_inject.txt", $"pid={pid} inject={r}\n"); } catch { }
                }) { IsBackground = true }.Start();

            // Botão SEGUE HABILITADO: troca o login e clique START de novo p/ abrir uma 2ª conta na mesma máquina.
            Status($"Rakion iniciado ({user}). Para uma 2ª conta: troque o login e clique START de novo.", false);
        }
        catch (Exception ex) { Status(ex.Message, true); }
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
