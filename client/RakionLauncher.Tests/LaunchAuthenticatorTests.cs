using System.Net;
using System.Text;
using RakionLauncher;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class LaunchAuthenticatorTests
{
    [Fact]
    public async Task TicketModeReturnsShortServerCredentialWithoutDowngrade()
    {
        var handler = new AuthHandler(HttpStatusCode.OK,
            "{\"ticket\":\"Abcdefghij123456_-XY\",\"expiresAt\":\"2099-01-01T00:00:00Z\"," +
            "\"onlineFriends\":[{\"displayName\":\"Amigo\"}]}");
        var authenticator = new LaunchAuthenticator(new HttpClient(handler));

        LaunchAuthentication authentication = await authenticator.AuthenticateAsync(
            Config(ticketAuth: true), 259, "test", "secret");

        Assert.Equal("Abcdefghij123456_-XY", authentication.Credential);
        Assert.Equal("Amigo", Assert.Single(authentication.OnlineFriends).DisplayName);
        Assert.Contains("\"user\":\"test\"", handler.Body);
        Assert.Contains("\"password\":\"secret\"", handler.Body);
        Assert.Contains("\"appId\":11001", handler.Body);
        Assert.Contains("\"buildVersion\":259", handler.Body);
    }

    [Fact]
    public async Task TicketFailureDoesNotFallBackToReusablePassword()
    {
        var authenticator = new LaunchAuthenticator(new HttpClient(
            new AuthHandler(HttpStatusCode.ServiceUnavailable, "{}")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authenticator.AuthenticateAsync(
                Config(ticketAuth: true), 259, "test", "secret"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "account_in_use", "Esta conta já está aberta.")]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request", "Login/usuário inválido.")]
    [InlineData(HttpStatusCode.Unauthorized, "invalid_credentials", "Usuário ou senha inválidos.")]
    public async Task TicketFailureShowsSpecificLoginReason(
        HttpStatusCode status, string code, string expectedMessage)
    {
        var authenticator = new LaunchAuthenticator(new HttpClient(
            new AuthHandler(status, $"{{\"error\":\"{code}\"}}")));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authenticator.AuthenticateAsync(
                Config(ticketAuth: true), 259, "test", "secret"));

        Assert.Equal(expectedMessage, error.Message);
    }

    [Fact]
    public async Task DisabledTicketModeKeepsRolloutCompatibility()
    {
        var authenticator = new LaunchAuthenticator(new HttpClient(
            new AuthHandler(HttpStatusCode.InternalServerError, "{}")));

        LaunchAuthentication authentication = await authenticator.AuthenticateAsync(
            Config(ticketAuth: false), 258, "test", "secret");

        Assert.Equal("secret", authentication.Credential);
        Assert.Empty(authentication.OnlineFriends);
    }

    private static LauncherConfig Config(bool ticketAuth) =>
        new(false, ticketAuth, new Uri("http://127.0.0.1:18081/"), 11001, 258, "key.pem");

    private sealed class AuthHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" :
                await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
