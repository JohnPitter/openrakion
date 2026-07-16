using System.Net;
using System.Text.Json;

namespace RakionLauncher;

internal sealed record LauncherConfig(
    bool UpdatesEnabled, bool TicketAuthEnabled, Uri UpdateBaseUrl, int AppId, int BaseVersion,
    string PublicKeyPemPath)
{
    public static LauncherConfig Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "launcher.settings.json");
        if (!File.Exists(path))
            return new LauncherConfig(false, false, new Uri("http://127.0.0.1/"),
                11001, 258, "update-public.pem");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        bool enabled = Environment.GetEnvironmentVariable("RAKION_UPDATE_ENABLED") is string enabledValue
            ? bool.Parse(enabledValue)
            : root.GetProperty("updatesEnabled").GetBoolean();
        bool ticketAuth = Environment.GetEnvironmentVariable("RAKION_TICKET_AUTH_ENABLED") is string authValue
            ? bool.Parse(authValue)
            : root.TryGetProperty("ticketAuthEnabled", out JsonElement authElement) &&
                authElement.GetBoolean();
        var baseUrl = new Uri(Environment.GetEnvironmentVariable("RAKION_UPDATE_URL") ??
            root.GetProperty("updateBaseUrl").GetString()
            ?? throw new InvalidDataException("updateBaseUrl ausente."));
        ValidateEndpoint(baseUrl);
        int appId = ReadIntOverride("RAKION_UPDATE_APP_ID", root.GetProperty("appId").GetInt32());
        int version = ReadIntOverride("RAKION_UPDATE_BASE_VERSION", root.GetProperty("baseVersion").GetInt32());
        string key = Environment.GetEnvironmentVariable("RAKION_UPDATE_PUBLIC_KEY") ??
            root.GetProperty("publicKeyPemPath").GetString()
            ?? throw new InvalidDataException("publicKeyPemPath ausente.");
        if (appId <= 0 || version < 0 || string.IsNullOrWhiteSpace(key))
            throw new InvalidDataException("Configuração de update inválida.");
        return new LauncherConfig(enabled, ticketAuth, baseUrl, appId, version, key);
    }

    public string ResolvePublicKeyPath() => Path.GetFullPath(Path.IsPathRooted(PublicKeyPemPath)
        ? PublicKeyPemPath
        : Path.Combine(AppContext.BaseDirectory, PublicKeyPemPath));

    private static void ValidateEndpoint(Uri endpoint)
    {
        bool loopback = endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(endpoint.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
        if (!loopback && endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Update remoto exige HTTPS.");
        if (endpoint.Scheme is not ("http" or "https"))
            throw new InvalidDataException("Update exige endpoint HTTP(S).");
    }

    private static int ReadIntOverride(string name, int fallback) =>
        Environment.GetEnvironmentVariable(name) is string value
            ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : fallback;
}
