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
}
