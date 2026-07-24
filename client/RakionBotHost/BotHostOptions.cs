namespace RakionBotHost;

internal enum HeadlessPeerRole
{
    Joiner,
    Master
}

internal sealed record BotHostOptions(
    string ClientRoot, string User, string Credential, string ServerId,
    HeadlessPeerRole Role, int? FieldId, string RoomName, string WorldName)
{
    internal const string ClientRootVariable = "RAKION_CLIENT_ROOT";
    internal const string CredentialVariable = "OPENRAKION_BOT_CREDENTIAL";
    internal const string WorldVariable = "OPENRAKION_HEADLESS_WORLD";
    internal const string RoleVariable = "OPENRAKION_HEADLESS_ROLE";
    internal const string RoomVariable = "OPENRAKION_HEADLESS_ROOM";
    internal const string QuickJoinVariable = "OPENRAKION_HEADLESS_QUICK_JOIN";

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
        HeadlessPeerRole role = ReadRole(values);
        int? fieldId = role == HeadlessPeerRole.Joiner ? ReadField(values) : null;
        string roomName = role == HeadlessPeerRole.Master
            ? ReadRoom(values) : string.Empty;
        string worldName = ReadWorld(Read(values, "world"));
        return new BotHostOptions(
            Path.GetFullPath(clientRoot), user, credential, server,
            role, fieldId, roomName, worldName);
    }

    private static Dictionary<string, string> ParsePairs(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 0)
            throw new ArgumentException(
                "uso joiner: RakionBotHost --client-root <dir> --user <id> " +
                "--field <id|quick> --world <LevelsSV\\...\\map.wld>; uso master: " +
                "--client-root <dir> --user <id> --role master --room <nome> " +
                "--world <LevelsSV\\...\\map.wld>");
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

    private static HeadlessPeerRole ReadRole(IReadOnlyDictionary<string, string> values)
    {
        string value = Read(values, "role", "joiner");
        if (value.Equals("joiner", StringComparison.OrdinalIgnoreCase))
            return HeadlessPeerRole.Joiner;
        if (value.Equals("master", StringComparison.OrdinalIgnoreCase))
            return HeadlessPeerRole.Master;
        throw new ArgumentException("--role deve ser master ou joiner.");
    }

    private static int? ReadField(IReadOnlyDictionary<string, string> values)
    {
        string value = Read(values, "field");
        if (value.Equals("quick", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(value, out int fieldId) || fieldId <= 0 || fieldId > ushort.MaxValue)
            throw new ArgumentException("--field deve ser quick ou um inteiro entre 1 e 65535.");
        return fieldId;
    }

    private static string ReadRoom(IReadOnlyDictionary<string, string> values)
    {
        string value = values.TryGetValue("room", out string? configured)
            ? configured.Trim() : "";
        if (value.Length is < 1 or > 40 || value.Any(char.IsControl))
            throw new ArgumentException("--room deve ter entre 1 e 40 caracteres.");
        return value;
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

    private static string ReadWorld(string value)
    {
        string normalized = value.Replace('/', '\\');
        if (!normalized.StartsWith("LevelsSV\\", StringComparison.OrdinalIgnoreCase) ||
            !normalized.EndsWith(".wld", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
            throw new ArgumentException("--world deve apontar para LevelsSV\\...\\map.wld.");
        return normalized;
    }
}
