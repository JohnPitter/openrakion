using RakionServer.Common;

namespace RakionLauncher;

internal static class UpdateTransaction
{
    public static void Apply(
        string clientRoot, string stagingRoot, string backupRoot,
        IReadOnlyList<UpdateFileEntry> files, Action? commit = null)
    {
        var applied = new List<AppliedFile>();
        try
        {
            foreach (UpdateFileEntry file in files)
                ApplyOne(clientRoot, stagingRoot, backupRoot, file, applied);
            commit?.Invoke();
        }
        catch
        {
            Rollback(applied);
            throw;
        }
    }

    private static void ApplyOne(
        string clientRoot, string stagingRoot, string backupRoot,
        UpdateFileEntry file, ICollection<AppliedFile> applied)
    {
        string target = UpdatePathPolicy.ResolveUnderRoot(clientRoot, file.Path);
        RejectRunningExecutable(target);
        EnsureSafeParent(clientRoot, target);
        string backup = UpdatePathPolicy.ResolveUnderRoot(backupRoot, file.Path);
        bool existed = File.Exists(target);
        if (Directory.Exists(target))
            throw new IOException($"O destino é um diretório: {file.Path}");
        if (existed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Move(target, backup);
        }
        applied.Add(new AppliedFile(target, backup, existed));
        if (file.Operation == UpdateOperation.Replace)
        {
            string staged = UpdatePathPolicy.ResolveUnderRoot(stagingRoot, file.Path);
            if (!File.Exists(staged)) throw new FileNotFoundException("Arquivo de staging ausente.", staged);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(staged, target);
        }
    }

    private static void Rollback(IReadOnlyList<AppliedFile> applied)
    {
        List<Exception>? failures = null;
        for (int index = applied.Count - 1; index >= 0; index--)
        {
            AppliedFile file = applied[index];
            try
            {
                if (File.Exists(file.Target)) File.Delete(file.Target);
                if (file.Existed && File.Exists(file.Backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                    File.Move(file.Backup, file.Target);
                }
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }
        if (failures is not null)
            throw new AggregateException("Rollback do update falhou.", failures);
    }

    private static void EnsureSafeParent(string root, string target)
    {
        string? parent = Path.GetDirectoryName(target);
        if (parent is null) throw new IOException("Destino sem diretório pai.");
        string current = Path.GetFullPath(root);
        string relative = Path.GetRelativePath(current, parent);
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse point recusado: {current}");
        }
    }

    private static void RejectRunningExecutable(string target)
    {
        string? executable = Environment.ProcessPath;
        if (executable is not null && Path.GetFullPath(target).Equals(
            Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            throw new IOException("O launcher em execução não pode substituir a si próprio.");
    }

    private sealed record AppliedFile(string Target, string Backup, bool Existed);
}
