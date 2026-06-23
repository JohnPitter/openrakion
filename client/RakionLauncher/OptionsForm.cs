namespace RakionLauncher;

/// <summary>
/// Diálogo de game options (o "GAME OPTION" do launcher) — o que o diálogo original do load.bin tinha,
/// mas com o botão Aplicar FUNCIONANDO e a opção extra de MODO DE TELA (janela/sem-borda/cheia). Grava
/// direto no PersistentSymbols.ini (via <see cref="GameSettings"/>) no OK e no Aplicar.
/// </summary>
internal sealed class OptionsForm : Form
{
    private static readonly (string label, string mode)[] Modes =
    {
        ("Janela", WindowMode.Windowed),
        ("Janela sem borda (tela cheia)", WindowMode.Borderless),
        ("Tela cheia exclusiva", WindowMode.Fullscreen),
    };
    private static readonly (int w, int h)[] Resolutions =
    {
        (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440),
    };

    private readonly string _ini, _modeFile;
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _res = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TrackBar _effect = new() { Minimum = 0, Maximum = 100, TickFrequency = 10 };
    private readonly TrackBar _music = new() { Minimum = 0, Maximum = 100, TickFrequency = 10 };
    private readonly TrackBar _sens = new() { Minimum = 1, Maximum = 50 };
    private readonly CheckBox _invert = new() { Text = "Inverter mouse" };

    public GameSettings Result { get; private set; }

    public OptionsForm(GameSettings s, string iniPath, string modeFile)
    {
        Result = s; _ini = iniPath; _modeFile = modeFile;

        Text = "Game Options";
        Icon = Theme.LoadIcon("app.ico");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(420, 392);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Lilac;
        Font = new Font("Segoe UI", 9f);

        foreach (var m in Modes) _mode.Items.Add(m.label);
        foreach (var r in Resolutions) _res.Items.Add($"{r.w} x {r.h}");

        int y = 18;
        Section("Vídeo", ref y);
        Row("Modo de tela", _mode, ref y);
        Row("Resolução", _res, ref y);
        Section("Som", ref y);
        Row("Volume de efeitos", _effect, ref y);
        Row("Volume de música", _music, ref y);
        Section("Mouse", ref y);
        Row("Sensibilidade", _sens, ref y);
        _invert.SetBounds(150, y, 200, 24); _invert.ForeColor = Theme.Ink; _invert.BackColor = Color.Transparent; Controls.Add(_invert); y += 34;

        var ok = new Button { Text = "OK" }; Theme.StyleButton(ok, true); ok.SetBounds(150, 352, 80, 30);
        var apply = new Button { Text = "Aplicar" }; Theme.StyleButton(apply); apply.SetBounds(238, 352, 80, 30);
        var cancel = new Button { Text = "Cancelar" }; Theme.StyleButton(cancel); cancel.SetBounds(326, 352, 80, 30);
        ok.Click += (_, _) => { Save(); DialogResult = DialogResult.OK; Close(); };
        apply.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(ok); Controls.Add(apply); Controls.Add(cancel);

        LoadFrom(s);
    }

    private void Section(string title, ref int y)
    {
        Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(14, y), ForeColor = Theme.Dark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        y += 24;
    }

    private void Row(string label, Control ctl, ref int y)
    {
        Controls.Add(new Label { Text = label, AutoSize = false, Bounds = new Rectangle(28, y + 2, 116, 22), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink });
        ctl.SetBounds(150, y, 250, 24);
        if (ctl is TrackBar) ctl.BackColor = Theme.Lilac;
        Controls.Add(ctl);
        y += (ctl is TrackBar ? 40 : 32);
    }

    private void LoadFrom(GameSettings s)
    {
        _mode.SelectedIndex = Math.Max(0, Array.FindIndex(Modes, m => m.mode == s.DisplayMode));
        int ri = Array.FindIndex(Resolutions, r => r.w == s.ScreenWidth && r.h == s.ScreenHeight);
        _res.SelectedIndex = ri >= 0 ? ri : 3;
        _effect.Value = Math.Clamp((int)Math.Round(s.SoundVolume * 100), 0, 100);
        _music.Value = Math.Clamp((int)Math.Round(s.MusicVolume * 100), 0, 100);
        _sens.Value = Math.Clamp((int)Math.Round(s.MouseSensitivity * 10), 1, 50);
        _invert.Checked = s.InvertMouse;
    }

    private GameSettings Collect() => new()
    {
        DisplayMode = Modes[_mode.SelectedIndex].mode,
        ScreenWidth = Resolutions[_res.SelectedIndex].w,
        ScreenHeight = Resolutions[_res.SelectedIndex].h,
        SoundVolume = _effect.Value / 100.0,
        MusicVolume = _music.Value / 100.0,
        MouseSensitivity = _sens.Value / 10.0,
        InvertMouse = _invert.Checked,
        MouseAccel = Result.MouseAccel,
        Gamma = Result.Gamma,
    };

    private void Save()
    {
        Result = Collect();
        try { Result.Save(_ini, _modeFile); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Erro ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
