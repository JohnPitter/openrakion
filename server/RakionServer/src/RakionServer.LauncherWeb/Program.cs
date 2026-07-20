using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.LauncherWeb;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
LauncherWebConfig config = LauncherWebConfig.Load(
    builder.Configuration, builder.Environment.ContentRootPath);
builder.WebHost.UseUrls(config.Endpoint.ToString());
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<UpdateReleaseProvider>();
builder.Services.AddSingleton<LauncherTicketRepository>();
builder.Services.AddRateLimiter(options =>
{
    AddLoginRateLimit(options, "legacy-login");
    AddLoginRateLimit(options, "ticket-auth");
});
var app = builder.Build();
if (config.TicketAuthEnabled && config.EnsureTicketSchema)
    await app.Services.GetRequiredService<LauncherTicketRepository>().EnsureSchemaAsync();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapModernAuth();
app.MapModernUpdates();

if (config.LegacyEnabled)
{
    app.MapMethods("/launcherlogin.php", new[] { "GET", "POST" }, LauncherLogin)
        .RequireRateLimiting("legacy-login");
    app.MapMethods("/launcherlogin", new[] { "GET", "POST" }, LauncherLogin)
        .RequireRateLimiting("legacy-login");
    app.MapGet("/fetch.php", Fetch);
    app.MapGet("/fetch", Fetch);
}

Log.Ok("web", "LauncherWeb em {0}; legado={1}; auth ticket={2}; updates assinados={3}",
    config.Endpoint, config.LegacyEnabled, config.TicketAuthEnabled, config.UpdatesEnabled);
app.Run();

static void AddLoginRateLimit(RateLimiterOptions options, string policyName)
{
    options.AddPolicy(policyName, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
}

// ---- handlers ----

async Task<IResult> LauncherLogin(HttpRequest req)
{
    string user = await Param(req, "user");
    string passhex = await Param(req, "pass");
    string? pw = HexToLatin1(passhex);
    if (pw is null) { Log.Warn("web", "login pass inválido (hex) user={0}", user); return Text("[Error]: 1"); }
    try
    {
        await using var c = new MySqlConnection(config.ConnectionString!);
        await c.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT 1 FROM user WHERE id=@u AND password=@p", c);
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@p", pw);
        bool ok = await cmd.ExecuteScalarAsync() is not null;
        if (!ok) { Log.Warn("web", "login FAIL user={0}", user); return Text("[Error]: 1"); }
        string token = Sha1Hex(user + "freeclient" + passhex);
        Log.Ok("web", "login OK user={0}", user);
        return Text(token);
    }
    catch (Exception ex) { Log.Error("web", "login DB: {0}", ex.Message); return Text("[Error]: 1"); }
}

// QUERY_STRING = "AppId&Ver" (dois números). Espelha fetch.php: monta a lista de updates a aplicar.
async Task<IResult> Fetch(HttpRequest req)
{
    string qs = req.QueryString.HasValue ? req.QueryString.Value!.TrimStart('?') : "";
    var parts = qs.Split('&');
    if (parts.Length < 2 || !int.TryParse(parts[0], out int appId) || !int.TryParse(parts[1], out int ver))
        return Text("Error");
    try
    {
        await using var c = new MySqlConnection(config.ConnectionString!);
        await c.OpenAsync();

        int verLimit; string noticeUrl, fileUrl;
        await using (var appCmd = new MySqlCommand("SELECT VerLimit, NoticeUrl, FileUrl FROM fetchapp WHERE AppId=@a", c))
        {
            appCmd.Parameters.AddWithValue("@a", appId);
            await using var ar = await appCmd.ExecuteReaderAsync();
            if (!await ar.ReadAsync()) return Text("");        // app desconhecido -> nada a fazer
            verLimit = ar.GetInt32(0);
            noticeUrl = ar.IsDBNull(1) ? "" : ar.GetString(1);
            fileUrl = ar.IsDBNull(2) ? "" : ar.GetString(2);
        }
        if (verLimit == ver) return Text("");                  // atualizado
        if (verLimit < ver) return Text("Error");              // cliente "à frente" -> erro (fiel ao fetch.php)

        var sb = new StringBuilder();
        sb.Append('+').Append(noticeUrl).Append('\n');
        sb.Append('=').Append(fileUrl).Append('\n');
        await using (var sumCmd = new MySqlCommand(
            "SELECT COUNT(*), IFNULL(SUM(FileSize),0) FROM fetchfile WHERE FileVer>@v AND AppId=@a", c))
        {
            sumCmd.Parameters.AddWithValue("@v", ver);
            sumCmd.Parameters.AddWithValue("@a", appId);
            await using var sr = await sumCmd.ExecuteReaderAsync();
            await sr.ReadAsync();
            sb.Append('~').Append(sr.GetInt64(0)).Append(';').Append(sr.GetInt64(1)).Append(';').Append(verLimit).Append('\n');
        }
        await using (var fCmd = new MySqlCommand(
            "SELECT Command, FileDir, FileIns, FileVer, FileSize FROM fetchfile WHERE FileVer>@v AND AppId=@a ORDER BY FileVer", c))
        {
            fCmd.Parameters.AddWithValue("@v", ver);
            fCmd.Parameters.AddWithValue("@a", appId);
            await using var fr = await fCmd.ExecuteReaderAsync();
            while (await fr.ReadAsync())
                sb.Append(fr.GetString(0)).Append(';').Append(fr.GetString(1)).Append(';')
                  .Append(fr.GetString(2)).Append(';').Append(fr.GetInt32(3)).Append(';').Append(fr.GetInt64(4)).Append('\n');
        }
        Log.Ok("web", "fetch app={0} ver={1} -> update p/ v{2}", appId, ver, verLimit);
        return Text(sb.ToString());
    }
    catch (Exception ex) { Log.Error("web", "fetch DB: {0}", ex.Message); return Text("Error"); }
}

// ---- helpers ----

static IResult Text(string s) => Results.Text(s, "text/plain", Encoding.Latin1);

static async Task<string> Param(HttpRequest r, string key)
{
    if (r.Query.TryGetValue(key, out var q)) return q.ToString();
    if (r.HasFormContentType) { var f = await r.ReadFormAsync(); if (f.TryGetValue(key, out var v)) return v.ToString(); }
    return "";
}

static string Sha1Hex(string s)
{
    byte[] hash = SHA1.HashData(Encoding.Latin1.GetBytes(s));
    return Convert.ToHexStringLower(hash);
}

static string? HexToLatin1(string hex)
{
    try { return Encoding.Latin1.GetString(Convert.FromHexString(hex)); }
    catch { return null; }
}
