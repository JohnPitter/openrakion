namespace RakionLauncher;

internal sealed partial class MainForm
{
    private async Task RefreshOnlineFriendsAsync()
    {
        AuthenticatedAccount? account = _accounts.Active;
        if (account is null) return;
        IReadOnlyList<OnlineFriend>? friends = await _onlineFriendsClient.GetAsync(
            _launcherConfig.UpdateBaseUrl, account.User, account.Password);
        if (friends is null || IsDisposed || !ReferenceEquals(account, _accounts.Active)) return;
        account.OnlineFriends = friends;
        ShowOnlineFriends(friends);
    }

    private void ShowOnlineFriends(IReadOnlyList<OnlineFriend> friends)
    {
        _friendsTitle.Text = AuthenticatedTitle(_accounts.Active?.User ?? "", friends.Count);
        _onlineFriends.BeginUpdate();
        _onlineFriends.Items.Clear();
        foreach (OnlineFriend friend in friends)
            _onlineFriends.Items.Add(friend.DisplayName);
        if (friends.Count == 0) _onlineFriends.Items.Add("Nenhum amigo online");
        _onlineFriends.EndUpdate();
    }

    internal static string AuthenticatedTitle(string account, int onlineFriendCount) =>
        $"Conta: {account} · Amigos online ({onlineFriendCount})";

    private void BuildLoginAndButtons()
    {
        ConfigureInputLabel(_userLabel, 264);
        _user.SetBounds(22, 284, 210, 26);
        ConfigureInputLabel(_passLabel, 316);
        _pass.SetBounds(22, 336, 210, 26);
        Controls.AddRange([_userLabel, _user, _passLabel, _pass]);

        _accountSwitch.SetBounds(18, 260, 300, 25);
        _accountSwitch.SelectedIndexChanged += OnAccountSelected;
        Controls.Add(_accountSwitch);

        _friendsTitle.SetBounds(22, 290, 292, 22);
        _friendsTitle.ForeColor = Theme.Ink;
        _friendsTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _onlineFriends.SetBounds(18, 312, 300, 50);
        _onlineFriends.BackColor = Theme.Panel;
        _onlineFriends.ForeColor = Theme.Ink;
        Controls.AddRange([_friendsTitle, _onlineFriends]);

        ConfigureActionButton(_login, new Rectangle(366, 262, 120, 100), true, OnLogin);
        ConfigureActionButton(_switchAccount, new Rectangle(330, 262, 88, 100), false,
            OnChangeAccountMode);
        ConfigureActionButton(_play, new Rectangle(426, 262, 88, 100), true, OnPlay);
        ConfigureActionButton(_options, new Rectangle(522, 262, 92, 100), false, OnOptions);
        Controls.AddRange([_login, _switchAccount, _play, _options]);
    }

    private void ConfigureInputLabel(Label label, int y)
    {
        label.AutoSize = true;
        label.Location = new Point(22, y);
        label.ForeColor = Theme.Ink;
        label.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
    }

    private static void ConfigureActionButton(
        Button button, Rectangle bounds, bool primary, EventHandler handler)
    {
        button.Bounds = bounds;
        button.TextAlign = ContentAlignment.MiddleCenter;
        Theme.StyleButton(button, primary);
        button.Click += handler;
    }

    private async void OnLogin(object? sender, EventArgs e)
    {
        _login.Enabled = false;
        try
        {
            string user = _user.Text.Trim();
            if (user.Length == 0) { Status("Informe o usuário.", true); return; }
            if (_pass.Text.Length == 0) { Status("Informe a senha.", true); return; }
            int version = UpdateClient.GetInstalledVersion(
                _clientDir, _launcherConfig.BaseVersion);
            LaunchAuthentication authentication = await AuthenticateAsync(
                user, _pass.Text, version);
            _accounts.AddOrUpdate(user, _pass.Text, authentication, version);
            _user.Clear();
            _pass.Clear();
            ShowAuthenticatedState();
            ShowOnlineFriends(authentication.OnlineFriends);
            Status($"Login realizado como {user}.", false);
        }
        catch (Exception exception)
        {
            Status(exception.Message, true);
        }
        finally { _login.Enabled = true; }
    }

    private async Task<LaunchAuthentication> EnsureAuthenticationAsync(
        AuthenticatedAccount account, int buildVersion)
    {
        if (account.Authentication is null || account.BuildVersion != buildVersion ||
            account.Authentication.ExpiresAt <= DateTime.UtcNow.AddSeconds(10))
        {
            LaunchAuthentication authentication = await AuthenticateAsync(
                account.User, account.Password, buildVersion);
            account.Update(account.Password, authentication, buildVersion);
            if (ReferenceEquals(account, _accounts.Active))
                ShowOnlineFriends(authentication.OnlineFriends);
        }
        return account.Authentication ??
            throw new InvalidOperationException("A autenticação da conta não foi concluída.");
    }

    private Task<LaunchAuthentication> AuthenticateAsync(
        string user, string password, int buildVersion) =>
        new LaunchAuthenticator().AuthenticateAsync(
            _launcherConfig, buildVersion, user, password);

    private void ShowLoginState(bool clearCredentials = false)
    {
        if (clearCredentials) { _user.Clear(); _pass.Clear(); }
        _userLabel.Visible = _user.Visible = _passLabel.Visible = _pass.Visible = true;
        _login.Visible = true;
        bool canReturn = _accounts.Active is not null;
        _login.Bounds = canReturn
            ? new Rectangle(426, 262, 88, 100)
            : new Rectangle(366, 262, 120, 100);
        _switchAccount.Text = "VOLTAR";
        _switchAccount.Visible = canReturn;
        _accountSwitch.Visible = _friendsTitle.Visible = _onlineFriends.Visible = false;
        _play.Visible = _options.Visible = false;
        if (clearCredentials) _user.Focus();
    }

    private void ShowAuthenticatedState()
    {
        _userLabel.Visible = _user.Visible = _passLabel.Visible = _pass.Visible = false;
        _login.Visible = false;
        _friendsTitle.Visible = _onlineFriends.Visible = true;
        _switchAccount.Text = "OUTRA\nCONTA";
        _switchAccount.Visible = _play.Visible = _options.Visible = true;
        RefreshAccountSwitch();
    }

    private void OnChangeAccountMode(object? sender, EventArgs eventArgs)
    {
        if (_login.Visible && _accounts.Active is AuthenticatedAccount account)
        {
            ShowAuthenticatedState();
            ShowOnlineFriends(account.OnlineFriends);
            Status($"Conta ativa: {account.User}.", false);
            return;
        }

        ShowLoginState(clearCredentials: true);
    }

    private async void OnAccountSelected(object? sender, EventArgs eventArgs)
    {
        if (_updatingAccountSwitch || _accountSwitch.SelectedItem is not string user ||
            !_accounts.Activate(user))
            return;

        AuthenticatedAccount account = _accounts.Active!;
        ShowOnlineFriends(account.OnlineFriends);
        Status($"Conta ativa: {account.User}.", false);
        await RefreshOnlineFriendsAsync();
    }

    private void RefreshAccountSwitch()
    {
        _updatingAccountSwitch = true;
        _accountSwitch.Items.Clear();
        _accountSwitch.Items.AddRange(_accounts.Users.Cast<object>().ToArray());
        _accountSwitch.SelectedItem = _accounts.Active?.User;
        _accountSwitch.Visible = _accounts.HasMultiple;
        if (_accounts.HasMultiple)
        {
            _friendsTitle.SetBounds(22, 290, 292, 22);
            _onlineFriends.SetBounds(18, 312, 300, 50);
        }
        else
        {
            _friendsTitle.SetBounds(22, 262, 292, 22);
            _onlineFriends.SetBounds(18, 284, 300, 78);
        }
        _updatingAccountSwitch = false;
    }
}
