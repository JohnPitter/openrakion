using System.Text;

namespace RakionServer.Admin;

public static class ClanPolicy
{
    public const int MaxMembers = 99;
    public const int MaxTreeChildren = 7;

    public static string NormalizeName(string value)
    {
        string name = value.Trim();
        int bytes = Encoding.ASCII.GetByteCount(name);
        if (bytes is < 1 or > 12 || name.Any(character =>
                character > 0x7F || !(char.IsLetterOrDigit(character) ||
                    character is ' ' or '_' or '-')))
            throw new ArgumentException(
                "Nome do clã deve ter 1..12 caracteres ASCII: letras, números, espaço, _ ou -.");
        return name;
    }

    public static string NormalizeAccount(string value)
    {
        string account = value.Trim();
        int bytes = Encoding.ASCII.GetByteCount(account);
        if (bytes is < 1 or > 16 || account.Any(character => character is < '!' or > '~'))
            throw new ArgumentException(
                "Conta deve ter 1..16 caracteres ASCII imprimíveis, sem espaços.");
        return account;
    }

    public static string NormalizeOptionalAccount(string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : NormalizeAccount(value);

    public static void ValidateParent(string account, string parent)
    {
        if (string.Equals(account, parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Um membro não pode ser o próprio superior.");
    }
}
