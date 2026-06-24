namespace RakionLauncher;

/// <summary>
/// Endereço do servidor de auth web (RakionServer.LauncherWeb, :80 — /launcherlogin e /register). Lido do
/// arquivo <c>server.host</c> no diretório do cliente (1 linha: IP ou host[:porta]); default 127.0.0.1
/// (servidor na MESMA máquina). O pacote do carlox traz o <c>server.host</c> com o IP do servidor — o mesmo
/// IP que o <c>config.xfs</c> aponta p/ broker/world. Config explícita do launcher: evita portar o parser XFS
/// só p/ descobrir o host.
/// </summary>
internal static class ServerConfig
{
    public const string DefaultHost = "127.0.0.1";
    private const string HostFile = "server.host";

    /// <summary>Host do auth web (sem esquema). Lê <c>server.host</c> do diretório do cliente; cai no default.</summary>
    public static string AuthHost(string clientDir)
    {
        try
        {
            string f = Path.Combine(clientDir, HostFile);
            if (File.Exists(f))
            {
                string h = File.ReadAllText(f).Trim();
                if (h.Length > 0) return h;
            }
        }
        catch { /* arquivo ilegível -> default */ }
        return DefaultHost;
    }

    /// <summary>URL base http do auth web (sem barra final).</summary>
    public static string BaseUrl(string clientDir) => $"http://{AuthHost(clientDir)}";
}
