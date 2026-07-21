namespace RakionLauncher;

internal sealed partial class MainForm
{
    private async Task RefreshOnlineFriendsAsync()
    {
        if (_authenticatedUser is null || _authenticatedPassword is null) return;
        IReadOnlyList<OnlineFriend>? friends = await _onlineFriendsClient.GetAsync(
            _launcherConfig.UpdateBaseUrl, _authenticatedUser, _authenticatedPassword);
        if (friends is null || IsDisposed) return;
        ShowOnlineFriends(friends);
    }

    private void ShowOnlineFriends(IReadOnlyList<OnlineFriend> friends)
    {
        _friendsTitle.Text = $"Amigos online ({friends.Count})";
        _onlineFriends.BeginUpdate();
        _onlineFriends.Items.Clear();
        foreach (OnlineFriend friend in friends)
            _onlineFriends.Items.Add(friend.DisplayName);
        if (friends.Count == 0) _onlineFriends.Items.Add("Nenhum amigo online");
        _onlineFriends.EndUpdate();
    }

    private void BuildLoginAndButtons()
    {
        ConfigureInputLabel(_userLabel, 264);
        _user.SetBounds(22, 284, 210, 26);
        ConfigureInputLabel(_passLabel, 316);
        _pass.SetBounds(22, 336, 210, 26);
        Controls.AddRange([_userLabel, _user, _passLabel, _pass]);

        _friendsTitle.SetBounds(22, 262, 292, 22);
        _friendsTitle.ForeColor = Theme.Ink;
        _friendsTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _onlineFriends.SetBounds(18, 284, 300, 78);
        _onlineFriends.BackColor = Theme.Panel;
        _onlineFriends.ForeColor = Theme.Ink;
        Controls.AddRange([_friendsTitle, _onlineFriends]);

        ConfigureActionButton(_login, new Rectangle(366, 262, 120, 100), true, OnLogin);
        ConfigureActionButton(_switchAccount, new Rectangle(330, 262, 88, 100), false,
            (_, _) => ShowLoginState(clearCredentials: true));
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
            _authenticatedUser = user;
            _authenticatedPassword = _pass.Text;
            int version = UpdateClient.GetInstalledVersion(
                _clientDir, _launcherConfig.BaseVersion);
            await RefreshAuthenticationAsync(version);
            ShowAuthenticatedState();
            Status($"Login realizado como {user}.", false);
        }
        catch (Exception exception)
        {
            _authenticatedUser = null;
            _authenticatedPassword = null;
            Status(exception.Message, true);
        }
        finally { _login.Enabled = true; }
    }

    private async Task<LaunchAuthentication> EnsureAuthenticationAsync(int buildVersion)
    {
        if (_authentication is null || _authenticatedBuildVersion != buildVersion ||
            _authentication.ExpiresAt <= DateTime.UtcNow.AddSeconds(10))
            await RefreshAuthenticationAsync(buildVersion);
        return _authentication!;
    }

    private async Task RefreshAuthenticationAsync(int buildVersion)
    {
        if (_authenticatedUser is null || _authenticatedPassword is null)
            throw new InvalidOperationException("Faça login antes de iniciar o jogo.");
        _authentication = await new LaunchAuthenticator().AuthenticateAsync(
            _launcherConfig, buildVersion, _authenticatedUser, _authenticatedPassword);
        _authenticatedBuildVersion = buildVersion;
        ShowOnlineFriends(_authentication.OnlineFriends);
    }

    private void ShowLoginState(bool clearCredentials = false)
    {
        _authentication = null;
        _authenticatedUser = null;
        _authenticatedPassword = null;
        if (clearCredentials) { _user.Clear(); _pass.Clear(); }
        _userLabel.Visible = _user.Visible = _passLabel.Visible = _pass.Visible = true;
        _login.Visible = true;
        _friendsTitle.Visible = _onlineFriends.Visible = false;
        _switchAccount.Visible = _play.Visible = _options.Visible = false;
        if (clearCredentials) _user.Focus();
    }

    private void ShowAuthenticatedState()
    {
        _userLabel.Visible = _user.Visible = _passLabel.Visible = _pass.Visible = false;
        _login.Visible = false;
        _friendsTitle.Visible = _onlineFriends.Visible = true;
        _switchAccount.Visible = _play.Visible = _options.Visible = true;
    }
}
