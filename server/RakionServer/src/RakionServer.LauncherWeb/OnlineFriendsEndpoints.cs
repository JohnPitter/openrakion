using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public static class OnlineFriendsEndpoints
{
    public static void MapOnlineFriends(this WebApplication app)
    {
        LauncherWebConfig config = app.Services.GetRequiredService<LauncherWebConfig>();
        if (!config.TicketAuthEnabled) return;
        app.MapPost("/api/v1/friends/online", GetOnlineFriendsAsync)
            .RequireRateLimiting("friend-status");
    }

    private static async Task<IResult> GetOnlineFriendsAsync(
        FriendStatusRequest request, LauncherTicketRepository repository,
        CancellationToken cancellationToken)
    {
        string account = request.User?.Trim() ?? "";
        string password = request.Password ?? "";
        if (account.Length is < 1 or > 16 || password.Length is < 1 or > 128)
            return Results.BadRequest(new FriendStatusError("invalid_request"));
        IReadOnlyList<OnlineLauncherFriend>? friends;
        try
        {
            friends = await repository.AuthenticateFriendsAsync(
                account, password, cancellationToken);
        }
        catch (Exception error)
        {
            Log.Error("web", "falha ao consultar amigos para user={0}: {1}",
                account, error.GetType().Name);
            return Results.Json(
                new FriendStatusError("service_unavailable"), statusCode: 503);
        }
        if (friends is null)
        {
            Log.Warn("web", "consulta de amigos recusada para user={0}", account);
            return Results.Json(
                new FriendStatusError("invalid_credentials"), statusCode: 401);
        }
        return Results.Ok(new FriendStatusResponse(
            friends.Select(friend => new FriendResponse(friend.DisplayName))));
    }

    private sealed record FriendStatusRequest(string? User, string? Password);
    private sealed record FriendStatusResponse(IEnumerable<FriendResponse> Friends);
    private sealed record FriendResponse(string DisplayName);
    private sealed record FriendStatusError(string Error);
}
