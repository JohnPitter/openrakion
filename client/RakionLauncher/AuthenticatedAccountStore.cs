namespace RakionLauncher;

internal sealed class AuthenticatedAccount
{
    public AuthenticatedAccount(
        string user, string password, LaunchAuthentication authentication, int buildVersion)
    {
        User = user;
        Password = password;
        UpdateAuthentication(authentication, buildVersion);
    }

    public string User { get; }
    public string Password { get; private set; }
    public LaunchAuthentication? Authentication { get; set; }
    public int BuildVersion { get; set; }
    public IReadOnlyList<OnlineFriend> OnlineFriends { get; set; } = Array.Empty<OnlineFriend>();

    public void Update(
        string password, LaunchAuthentication authentication, int buildVersion)
    {
        Password = password;
        UpdateAuthentication(authentication, buildVersion);
    }

    private void UpdateAuthentication(LaunchAuthentication authentication, int buildVersion)
    {
        Authentication = authentication;
        BuildVersion = buildVersion;
        OnlineFriends = authentication.OnlineFriends;
    }
}

internal sealed class AuthenticatedAccountStore
{
    private readonly Dictionary<string, AuthenticatedAccount> _accounts =
        new(StringComparer.OrdinalIgnoreCase);

    public AuthenticatedAccount? Active { get; private set; }
    public IReadOnlyList<string> Users => _accounts.Values.Select(account => account.User).ToArray();
    public bool HasMultiple => _accounts.Count > 1;

    public AuthenticatedAccount AddOrUpdate(
        string user, string password, LaunchAuthentication authentication, int buildVersion)
    {
        if (_accounts.TryGetValue(user, out AuthenticatedAccount? account))
            account.Update(password, authentication, buildVersion);
        else
        {
            account = new AuthenticatedAccount(user, password, authentication, buildVersion);
            _accounts.Add(user, account);
        }

        Active = account;
        return account;
    }

    public bool Activate(string user)
    {
        if (!_accounts.TryGetValue(user, out AuthenticatedAccount? account)) return false;
        Active = account;
        return true;
    }
}
