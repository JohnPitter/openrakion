using System.Text;

namespace RakionClientRuntime;

public static class LegacyClientCredentials
{
    public static string EncodeAsciiHex(string credential)
    {
        if (string.IsNullOrEmpty(credential))
            throw new ArgumentException("Credencial vazia.", nameof(credential));
        return Convert.ToHexString(Encoding.ASCII.GetBytes(credential)).ToLowerInvariant();
    }
}
