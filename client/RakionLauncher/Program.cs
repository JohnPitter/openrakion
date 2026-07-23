namespace RakionLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0].Equals("--update-only", StringComparison.OrdinalIgnoreCase))
            return RunUpdateOnly(args[1]);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunUpdateOnly(string clientRoot)
    {
        try
        {
            LauncherConfig config = LauncherConfig.Load();
            var progress = new Progress<string>(Console.WriteLine);
            int version = new UpdateClient().ApplyLatestAsync(
                Path.GetFullPath(clientRoot), config, progress).GetAwaiter().GetResult();
            Console.WriteLine($"Update concluído na versão {version}.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
