using System.Net.Http.Json;
using RakionServer.Common;

namespace RakionLauncher;

internal sealed class LaunchAuthenticator
{
    private readonly HttpClient _client;

    public LaunchAuthenticator(HttpClient? client = null) =>
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string> GetCredentialAsync(
        LauncherConfig config, int buildVersion, string user, string password,
        CancellationToken cancellationToken = default)
    {
        if (!config.TicketAuthEnabled) return password;

        Uri endpoint = new(config.UpdateBaseUrl, "api/v1/auth/ticket");
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            endpoint, new TicketRequest(
                user, password, config.AppId, buildVersion), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Usuário ou senha inválidos."
                : "O serviço de autenticação recusou o login.");

        TicketResponse? result = await response.Content.ReadFromJsonAsync<TicketResponse>(
            cancellationToken: cancellationToken);
        if (result is null || !LauncherTicketToken.IsValidFormat(result.Ticket) ||
            result.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidDataException("O serviço retornou um ticket inválido.");
        return result.Ticket;
    }

    private sealed record TicketRequest(
        string User, string Password, int AppId, int BuildVersion);
    private sealed record TicketResponse(string Ticket, DateTime ExpiresAt);
}
