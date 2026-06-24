namespace RakionLauncher;

/// <summary>
/// Diálogo "Criar conta" — registra uma conta nova direto pelo launcher (POST /register do auth web), sem
/// precisar do painel admin. As regras de id/senha (3–16; id alfanumérico) são VALIDADAS NO SERVIDOR
/// (<c>AccountStore</c>, fonte única) — aqui só checo "senha == confirmação" antes de bater na rede.
/// No sucesso, expõe <see cref="CreatedId"/> p/ o <see cref="MainForm"/> preencher o login.
/// </summary>
internal sealed class RegisterForm : Form
{
    private readonly string _baseUrl;
    private readonly TextBox _id = new();
    private readonly TextBox _pw = new() { UseSystemPasswordChar = true };
    private readonly TextBox _pw2 = new() { UseSystemPasswordChar = true };
    private readonly Button _create = new() { Text = "Criar conta" };
    private readonly Button _close = new() { Text = "Cancelar" };
    private readonly Label _status = new();

    /// <summary>ID criado com sucesso (null se nenhuma conta foi criada).</summary>
    public string? CreatedId { get; private set; }

    public RegisterForm(string baseUrl)
    {
        _baseUrl = baseUrl;

        Text = "Criar conta";
        Icon = Theme.LoadIcon("app.ico");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(372, 232);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Lilac;
        Font = new Font("Segoe UI", 9f);

        int y = 22;
        Field("Login ID", _id, ref y);
        Field("Senha", _pw, ref y);
        Field("Confirmar senha", _pw2, ref y);

        Theme.StyleButton(_create, true); _create.SetBounds(150, y + 8, 110, 30); _create.Click += OnCreate;
        Theme.StyleButton(_close); _close.SetBounds(266, y + 8, 88, 30);
        _close.Click += (_, _) => { DialogResult = CreatedId != null ? DialogResult.OK : DialogResult.Cancel; Close(); };
        Controls.Add(_create); Controls.Add(_close);

        _status.SetBounds(16, y + 48, 340, 38); _status.ForeColor = Theme.Ink; Controls.Add(_status);
        AcceptButton = _create;
    }

    private void Field(string label, TextBox box, ref int y)
    {
        Controls.Add(new Label
        {
            Text = label, AutoSize = false, Bounds = new Rectangle(16, y + 2, 120, 22),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink
        });
        box.SetBounds(140, y, 214, 24);
        Controls.Add(box);
        y += 36;
    }

    private async void OnCreate(object? sender, EventArgs e)
    {
        string id = _id.Text.Trim();
        if (id.Length == 0) { Status("Informe o ID.", true); return; }
        if (_pw.Text.Length == 0) { Status("Informe a senha.", true); return; }
        if (_pw.Text != _pw2.Text) { Status("As senhas não conferem.", true); return; }

        _create.Enabled = false;
        Status("Criando conta…", false);
        var res = await AuthClient.RegisterAsync(_baseUrl, id, GameLauncher.HexPass(_pw.Text));
        _create.Enabled = true;
        switch (res)
        {
            case AuthClient.RegisterResult.Created:
                CreatedId = id;
                _id.Enabled = _pw.Enabled = _pw2.Enabled = false;
                _create.Visible = false; _close.Text = "Fechar";
                Status($"Conta '{id}' criada! Feche e faça login.", false);
                break;
            case AuthClient.RegisterResult.Exists: Status("Esse ID já existe. Escolha outro.", true); break;
            case AuthClient.RegisterResult.InvalidId: Status("ID inválido (3 a 16 letras/números).", true); break;
            case AuthClient.RegisterResult.InvalidPassword: Status("Senha inválida (3 a 16 caracteres).", true); break;
            case AuthClient.RegisterResult.Unreachable: Status("Servidor indisponível. O servidor está no ar?", true); break;
            default: Status("Não foi possível criar a conta.", true); break;
        }
    }

    private void Status(string msg, bool error) { _status.ForeColor = error ? Color.Firebrick : Theme.Ink; _status.Text = msg; }
}
