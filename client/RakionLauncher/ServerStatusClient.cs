using System.Net.Http.Json;
using System.Text.Json;

namespace RakionLauncher;

internal sealed record ServerStatusResponse(bool Online, int OnlinePlayers, int Capacity);

internal sealed class ServerStatusClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<ServerStatusResponse?> GetAsync(
        Uri baseUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ServerStatusResponse>(
                new Uri(baseUrl, "api/v1/server-status"), cancellationToken);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }
}
