using RakionLauncher;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class MainFormAuthenticationTests
{
    [Fact]
    public void AuthenticatedTitleIdentifiesAccountAndOnlineFriends()
    {
        string title = MainForm.AuthenticatedTitle("test2", 3);

        Assert.Equal("Conta: test2 · Amigos online (3)", title);
    }

    [Fact]
    public void AccountStoreSwitchesBetweenAuthenticatedAccounts()
    {
        var store = new AuthenticatedAccountStore();
        var authentication = new LaunchAuthentication(
            "ticket", DateTime.UtcNow.AddMinutes(10), Array.Empty<OnlineFriend>());

        store.AddOrUpdate("account1", "pass1", authentication, 258);
        store.AddOrUpdate("account2", "pass2", authentication, 258);

        Assert.True(store.HasMultiple);
        Assert.Equal("account2", store.Active?.User);
        Assert.True(store.Activate("ACCOUNT1"));
        Assert.Equal("account1", store.Active?.User);
        AuthenticatedAccount? other = store.GetAvailableOtherThan(
            store.Active!, Array.Empty<string>());
        Assert.NotNull(other);
        Assert.Equal("account2", other.User);
        Assert.Null(store.GetAvailableOtherThan(
            store.Active!, ["ACCOUNT2"]));
    }
}
