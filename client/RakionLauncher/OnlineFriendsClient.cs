using System.Net.Http.Json;
using System.Text.Json;

namespace RakionLauncher;

internal sealed class OnlineFriendsClient
{
    private readonly HttpClient _http;

    public OnlineFriendsClient(HttpClient? http = null) =>
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<IReadOnlyList<OnlineFriend>?> GetAsync(
        Uri baseUrl, string user, string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                new Uri(baseUrl, "api/v1/friends/online"),
                new FriendStatusRequest(user, password), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            FriendStatusResponse? result = await response.Content
                .ReadFromJsonAsync<FriendStatusResponse>(cancellationToken);
            return result?.Friends;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    private sealed record FriendStatusRequest(string User, string Password);
    private sealed record FriendStatusResponse(OnlineFriend[] Friends);
}
