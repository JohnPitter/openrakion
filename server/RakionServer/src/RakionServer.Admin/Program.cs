using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using RakionServer.Admin;
using RakionServer.Admin.Components;
using RakionServer.Common;

var builder = WebApplication.CreateBuilder(args);
string adminPass = builder.Configuration["Admin:Password"] ?? "";
if (adminPass.Length < 16)
    throw new InvalidOperationException(
        "Defina Admin__Password com pelo menos 16 caracteres.");
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Rakion")))
    throw new InvalidOperationException("Defina ConnectionStrings__Rakion.");
Uri adminEndpoint = AdminEndpointPolicy.Validate(
    builder.Configuration["Admin:Url"] ?? "http://127.0.0.1:8080");
builder.WebHost.UseUrls(adminEndpoint.ToString());
AdminIdentity adminIdentity = AdminIdentity.FromConfiguration(builder.Configuration);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<AdminDb>();
builder.Services.AddSingleton(adminIdentity);
builder.Services.AddSingleton<ItemNames>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(2);
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = adminEndpoint.Scheme == Uri.UriSchemeHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRateLimiter(options => options.AddPolicy("login", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));

var app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    AdminDb database = scope.ServiceProvider.GetRequiredService<AdminDb>();
    await database.EnsureCurrencySchemaAsync();
    await database.EnsureAuditSchemaAsync();
}
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.UseStaticFiles();   // serve wwwroot/icons/<itemid>.png (biblioteca de ícones capturados)
app.MapStaticAssets();

app.MapPost("/auth/login", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(adminPass));
    byte[] supplied = SHA256.HashData(Encoding.UTF8.GetBytes(form["password"].ToString()));
    if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
    {
        Log.Warn("admin", "login recusado de {0}",
            ctx.Connection.RemoteIpAddress?.ToString() ?? "?");
        return Results.Redirect("/login?e=1");
    }
    var ident = new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Name, adminIdentity.OperatorId),
        new Claim(ClaimTypes.Role, adminIdentity.Role.ToString())
    },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(ident));
    Log.Ok("admin", "login OK de {0}", ctx.Connection.RemoteIpAddress?.ToString() ?? "?");
    return Results.Redirect("/");
}).RequireRateLimiting("login");

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

Log.Ok("admin", "Painel Admin (.NET) iniciado para operador={0}, papel={1}",
    adminIdentity.OperatorId, adminIdentity.Role);
app.Run();
