namespace RakionBotHost;

internal sealed record BotHostOptions(
    string ClientRoot, string User, string Credential, string ServerId, int FieldId)
{
    internal const string ClientRootVariable = "RAKION_CLIENT_ROOT";
    internal const string CredentialVariable = "OPENRAKION_BOT_CREDENTIAL";

    public static BotHostOptions Parse(string[] args)
    {
        Dictionary<string, string> values = ParsePairs(args);
        string clientRoot = ReadPath(values, "client-root",
            Environment.GetEnvironmentVariable(ClientRootVariable));
        string credential = Environment.GetEnvironmentVariable(CredentialVariable) ?? "";
        if (credential.Length == 0)
            throw new InvalidOperationException($"{CredentialVariable} não configurada.");
        string user = Read(values, "user");
        string server = Read(values, "server", "1A");
        if (!int.TryParse(Read(values, "field"), out int fieldId) || fieldId <= 0)
            throw new ArgumentException("--field deve ser um inteiro positivo.");
        return new BotHostOptions(
            Path.GetFullPath(clientRoot), user, credential, server, fieldId);
    }

    private static Dictionary<string, string> ParsePairs(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 0)
            throw new ArgumentException(
                "uso: RakionBotHost --client-root <dir> --user <id> --field <id> [--server <id>]");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index += 2)
        {
            string name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException($"Argumento inválido: {name}");
            values[name[2..]] = args[index + 1];
        }
        return values;
    }

    private static string Read(
        IReadOnlyDictionary<string, string> values, string name, string? fallback = null)
    {
        string value = values.TryGetValue(name, out string? configured)
            ? configured : fallback ?? "";
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
            throw new ArgumentException($"--{name} ausente ou inválido.");
        return value;
    }

    private static string ReadPath(
        IReadOnlyDictionary<string, string> values, string name, string? fallback)
    {
        string value = values.TryGetValue(name, out string? configured)
            ? configured : fallback ?? "";
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"--{name} ausente ou inválido.");
        return value;
    }
}
