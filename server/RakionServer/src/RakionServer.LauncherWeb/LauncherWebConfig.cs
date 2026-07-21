using System.Net;

namespace RakionServer.LauncherWeb;

public sealed record LauncherWebConfig(
    Uri Endpoint, bool LegacyEnabled, bool UpdatesEnabled, bool TicketAuthEnabled,
    bool EnsureTicketSchema, int TicketLifetimeSeconds, string? ConnectionString, string ContentRoot,
    string? SigningPrivateKeyPem)
{
    public static LauncherWebConfig Load(IConfiguration configuration, string contentRoot)
    {
        Uri endpoint = ValidateEndpoint(configuration["LauncherWeb:Url"] ??
            "http://127.0.0.1:80");
        bool legacy = configuration.GetValue("Legacy:Enabled", true);
        bool updates = configuration.GetValue("Updates:Enabled", false);
        bool ticketAuth = configuration.GetValue("Auth:Enabled", false);
        bool ensureTicketSchema = configuration.GetValue("Auth:EnsureSchema", true);
        int ticketLifetime = Math.Clamp(
            configuration.GetValue("Auth:TicketLifetimeSeconds", 900), 60, 1800);
        string? connectionString = configuration.GetConnectionString("Rakion");
        if ((legacy || ticketAuth) && string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings__Rakion é obrigatória quando Legacy ou Auth estiver habilitado.");

        string root = Path.GetFullPath(configuration["Updates:ContentRoot"] ??
            Path.Combine(contentRoot, "updates"));
        string? keyPath = configuration["Updates:SigningPrivateKeyPath"];
        string? privateKey = null;
        if (updates)
        {
            if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
                throw new InvalidOperationException(
                    "Updates__SigningPrivateKeyPath é obrigatório quando Updates__Enabled=true.");
            privateKey = File.ReadAllText(keyPath);
            Directory.CreateDirectory(root);
        }
        return new LauncherWebConfig(endpoint, legacy, updates, ticketAuth, ensureTicketSchema,
            ticketLifetime, connectionString, root, privateKey);
    }

    private static Uri ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("LauncherWeb__Url deve ser uma URL HTTP(S) absoluta.");
        bool loopback = endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(endpoint.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
        if (!loopback && endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Bind externo do LauncherWeb exige HTTPS.");
        return endpoint;
    }
}
