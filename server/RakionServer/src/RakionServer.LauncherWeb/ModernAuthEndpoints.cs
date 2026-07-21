using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public static class ModernAuthEndpoints
{
    public static void MapModernAuth(this WebApplication app)
    {
        LauncherWebConfig config = app.Services.GetRequiredService<LauncherWebConfig>();
        if (!config.TicketAuthEnabled) return;

        app.MapPost("/api/v1/auth/ticket", IssueTicketAsync)
            .RequireRateLimiting("ticket-auth");
    }

    private static async Task<IResult> IssueTicketAsync(
        TicketRequest request, LauncherTicketRepository repository,
        CancellationToken cancellationToken)
    {
        string account = request.User?.Trim() ?? "";
        string password = request.Password ?? "";
        if (account.Length is < 1 or > 16 || password.Length is < 1 or > 128 ||
            request.AppId <= 0 || request.BuildVersion < 0)
            return Results.BadRequest(new TicketError("invalid_request"));

        LauncherTicketIssueResult result;
        try
        {
            result = await repository.IssueAsync(
                account, password,
                new LauncherBuildIdentity(request.AppId, request.BuildVersion), cancellationToken);
        }
        catch (Exception error)
        {
            Log.Error("web", "falha ao emitir ticket para user={0}: {1}",
                account, error.GetType().Name);
            return Results.Json(new TicketError("service_unavailable"), statusCode: 503);
        }
        if (result.Status == LauncherTicketIssueStatus.InvalidCredentials)
        {
            Log.Warn("web", "ticket recusado para user={0}", account);
            return Results.Json(new TicketError("invalid_credentials"), statusCode: 401);
        }
        if (result.Status == LauncherTicketIssueStatus.AccountInUse)
        {
            Log.Warn("web", "ticket recusado: conta já conectada user={0}", account);
            return Results.Json(new TicketError("account_in_use"), statusCode: 409);
        }

        IssuedLauncherTicket issued = result.Ticket!;
        Log.Ok("web", "ticket emitido para user={0}, expira em {1:O}",
            account, issued.ExpiresAt);
        return Results.Ok(new TicketResponse(issued.Ticket, issued.ExpiresAt));
    }

    private sealed record TicketRequest(
        string? User, string? Password, int AppId, int BuildVersion);
    private sealed record TicketResponse(string Ticket, DateTime ExpiresAt);
    private sealed record TicketError(string Error);
}
