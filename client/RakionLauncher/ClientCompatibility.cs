namespace RakionLauncher;

internal static class ClientCompatibility
{
    private static readonly DeploymentFile[] Files =
    {
        new("RakionLauncher.Native.version.dll", "version.dll"),
        new("RakionLauncher.Native.RakionClientPatch.dll", "RakionClientPatch.dll")
    };

    internal static void Install(string binDir)
    {
        foreach (DeploymentFile file in Files)
            InstallFile(binDir, file);
        string legacyForwarder = Path.Combine(binDir, "verorig.dll");
        if (File.Exists(legacyForwarder)) File.Delete(legacyForwarder);
    }

    private static void InstallFile(string binDir, DeploymentFile file)
    {
        using Stream source = typeof(ClientCompatibility).Assembly.GetManifestResourceStream(file.ResourceName)
            ?? throw new InvalidOperationException($"recurso nativo ausente: {file.ResourceName}");
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        byte[] expected = memory.ToArray();
        string destination = Path.Combine(binDir, file.FileName);
        if (File.Exists(destination) && File.ReadAllBytes(destination).AsSpan().SequenceEqual(expected)) return;
        File.WriteAllBytes(destination, expected);
    }

    private sealed record DeploymentFile(string ResourceName, string FileName);
}
