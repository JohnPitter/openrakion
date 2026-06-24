namespace RakionLauncher;

/// <summary>
/// Cliente HTTP do auth web do launcher (RakionServer.LauncherWeb, :80). Valida o login via
/// <c>/launcherlogin</c> ANTES de lançar o jogo (aviso de credenciais inválidas): o endpoint responde o
/// token sha1 em sucesso ou um corpo <c>[Error]: N</c> em falha (sempre 200, diferenciado pelo corpo).
/// </summary>
internal static class AuthClient
{
    public enum LoginResult { Valid, Invalid, Unreachable }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Valida user/senha no <c>/launcherlogin</c>. A senha vai em HEX (mesmo esquema do argv do jogo,
    /// ver <see cref="GameLauncher.HexPass"/>). Falha de rede/timeout -> <see cref="LoginResult.Unreachable"/>
    /// (distinto de credencial inválida, p/ a mensagem certa).</summary>
    public static async Task<LoginResult> LoginAsync(string baseUrl, string user, string hexPass)
    {
        string url = $"{baseUrl}/launcherlogin?user={Uri.EscapeDataString(user)}&pass={hexPass}";
        try
        {
            string body = (await Http.GetStringAsync(url)).Trim();
            // token (sha1 hex, 40 chars) = sucesso; "[Error]: N" ou vazio = credencial inválida.
            return body.Length > 0 && !body.StartsWith("[Error", StringComparison.OrdinalIgnoreCase)
                ? LoginResult.Valid
                : LoginResult.Invalid;
        }
        catch { return LoginResult.Unreachable; }
    }
}
