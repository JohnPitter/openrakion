using System;
using System.IO;
using System.Linq;

namespace RakionServer.Common;

public static class UpdatePathPolicy
{
    public static string NormalizeRelative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240 ||
            Path.IsPathRooted(value) || value.Contains('\\') || value.Contains(':') ||
            value.Contains('\0'))
            throw new ArgumentException("Caminho de update inválido.", nameof(value));

        string[] segments = value.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new ArgumentException("Caminho de update inválido.", nameof(value));
        return string.Join('/', segments);
    }

    public static string ResolveUnderRoot(string root, string relative)
    {
        string normalized = NormalizeRelative(relative);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(fullRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Destino escapou da raiz de update.", nameof(relative));
        return target;
    }

    public static void RejectReparsePoints(string root, string target)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string? current = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
        while (current is not null && current.Length >= fullRoot.Length)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Update recusado através de reparse point: {current}");
            if (current.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) return;
            current = Path.GetDirectoryName(current);
        }
        throw new IOException("Destino de update não pertence à raiz esperada.");
    }
}
