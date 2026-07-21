using System.Net.Http.Json;
using System.Text.Json;
using RakionServer.Common;

namespace RakionLauncher;

internal sealed record OnlineFriend(string DisplayName);
internal sealed record LaunchAuthentication(
    string Credential, DateTime ExpiresAt, IReadOnlyList<OnlineFriend> OnlineFriends);

internal sealed class LaunchAuthenticator
{
    private readonly HttpClient _client;

    public LaunchAuthenticator(HttpClient? client = null) =>
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<LaunchAuthentication> AuthenticateAsync(
        LauncherConfig config, int buildVersion, string user, string password,
        CancellationToken cancellationToken = default)
    {
        if (!config.TicketAuthEnabled)
            return new LaunchAuthentication(
                password, DateTime.MaxValue, Array.Empty<OnlineFriend>());

        Uri endpoint = new(config.UpdateBaseUrl, "api/v1/auth/ticket");
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            endpoint, new TicketRequest(
                user, password, config.AppId, buildVersion), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string? error = await ReadErrorCodeAsync(response, cancellationToken);
            throw new InvalidOperationException(error switch
            {
                "account_in_use" => "Esta conta já está aberta.",
                "invalid_request" => "Login/usuário inválido.",
                "invalid_credentials" => "Usuário ou senha inválidos.",
                _ => "O serviço de autenticação recusou o login."
            });
        }

        TicketResponse? result = await response.Content.ReadFromJsonAsync<TicketResponse>(
            cancellationToken: cancellationToken);
        if (result is null || !LauncherTicketToken.IsValidFormat(result.Ticket) ||
            result.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidDataException("O serviço retornou um ticket inválido.");
        return new LaunchAuthentication(
            result.Ticket, result.ExpiresAt,
            result.OnlineFriends ?? Array.Empty<OnlineFriend>());
    }

    private sealed record TicketRequest(
        string User, string Password, int AppId, int BuildVersion);
    private sealed record TicketResponse(
        string Ticket, DateTime ExpiresAt, OnlineFriend[]? OnlineFriends);
    private sealed record TicketError(string Error);

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<TicketError>(
                cancellationToken: cancellationToken))?.Error;
        }
        catch (JsonException) { return null; }
    }
}
