namespace RakionLauncher;

/// <summary>
/// Cliente HTTP do auth web do launcher (RakionServer.LauncherWeb, :80). Valida o login via
/// <c>/launcherlogin</c> ANTES de lançar o jogo (aviso de credenciais inválidas): o endpoint responde o
/// token sha1 em sucesso ou um corpo <c>[Error]: N</c> em falha (sempre 200, diferenciado pelo corpo).
/// </summary>
internal static class AuthClient
{
    public enum LoginResult { Valid, Invalid, Unreachable }
    public enum RegisterResult { Created, Exists, InvalidId, InvalidPassword, Unreachable, Error }

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

    /// <summary>Cria conta via POST <c>/register</c> (user + pass HEX). Corpo "OK" = criada; "[Error]: motivo"
    /// mapeia o motivo (exists/invalidid/invalidpass); falha de rede/timeout -> Unreachable. As regras de id/senha
    /// são validadas no servidor (<c>AccountStore</c>) — fonte única.</summary>
    public static async Task<RegisterResult> RegisterAsync(string baseUrl, string user, string hexPass)
    {
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["user"] = user, ["pass"] = hexPass });
            using var resp = await Http.PostAsync($"{baseUrl}/register", form);
            string body = (await resp.Content.ReadAsStringAsync()).Trim();
            if (body.Equals("OK", StringComparison.OrdinalIgnoreCase)) return RegisterResult.Created;
            if (body.Contains("exists", StringComparison.OrdinalIgnoreCase)) return RegisterResult.Exists;
            if (body.Contains("invalidid", StringComparison.OrdinalIgnoreCase)) return RegisterResult.InvalidId;
            if (body.Contains("invalidpass", StringComparison.OrdinalIgnoreCase)) return RegisterResult.InvalidPassword;
            return RegisterResult.Error;
        }
        catch { return RegisterResult.Unreachable; }
    }
}
