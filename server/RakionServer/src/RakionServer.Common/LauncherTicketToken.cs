using System;
using System.Security.Cryptography;

namespace RakionServer.Common;

public static class LauncherTicketToken
{
    public const int RawSize = 15;
    public const int EncodedSize = 20;

    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[RawSize];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Hash(string token) =>
        SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));

    public static bool IsValidFormat(string token)
    {
        if (token.Length != EncodedSize) return false;
        foreach (char value in token)
            if (!(char.IsAsciiLetterOrDigit(value) || value is '-' or '_'))
                return false;
        return true;
    }
}
