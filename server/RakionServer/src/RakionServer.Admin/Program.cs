using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RakionServer.Admin;
using RakionServer.Admin.Components;
using RakionServer.Common;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<AdminDb>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => { o.LoginPath = "/login"; o.ExpireTimeSpan = TimeSpan.FromHours(8); });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseStaticFiles();   // serve wwwroot/icons/<itemid>.png (biblioteca de ícones capturados)
app.MapStaticAssets();

// senha única do painel (appsettings Admin:Password). Tool pessoal em localhost.
string adminPass = app.Configuration["Admin:Password"] ?? "rakion";

app.MapPost("/auth/login", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (form["password"].ToString() != adminPass) return Results.Redirect("/login?e=1");
    var ident = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "admin") },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(ident));
    Log.Ok("admin", "login OK de {0}", ctx.Connection.RemoteIpAddress?.ToString() ?? "?");
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

Log.Ok("admin", "Painel Admin (.NET) ouvindo na :8080");
app.Run();
