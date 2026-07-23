namespace RakionLauncher;

internal static class PuppetLaunch
{
    internal const string PasswordVariable = "RAKION_PUPPET_PASSWORD";

    public static int Run(string[] args)
    {
        try
        {
            PuppetLaunchOptions options = Parse(args);
            Launch(options);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal static PuppetLaunchOptions Parse(string[] args)
    {
        if (args.Length is < 2 or > 3 || string.IsNullOrWhiteSpace(args[1]))
            throw new ArgumentException(
                "uso: RakionLauncher.exe --puppet <usuario> [serverId]");
        string password = Environment.GetEnvironmentVariable(PasswordVariable) ?? "";
        if (password.Length == 0)
            throw new InvalidOperationException($"{PasswordVariable} não configurada.");
        return new PuppetLaunchOptions(
            args[1].Trim(), password, args.Length == 3 ? args[2] : MainForm.ServerId);
    }

    private static void Launch(PuppetLaunchOptions options)
    {
        string clientDir = MainForm.ResolveClientDir();
        string binDir = Path.Combine(clientDir, "Bin");
        ClientCompatibility.Install(binDir);
        ClientCompatibility.ValidateInstalled(binDir);

        LauncherConfig config = LauncherConfig.Load();
        int buildVersion = UpdateClient.GetInstalledVersion(clientDir, config.BaseVersion);
        LaunchAuthentication authentication = new LaunchAuthenticator().AuthenticateAsync(
            config, buildVersion, options.User, options.Password).GetAwaiter().GetResult();

        string iniPath = Path.Combine(clientDir, "Scripts", "PersistentSymbols.ini");
        string modeFile = Path.Combine(clientDir, "display.mode");
        GameSettings settings = GameSettings.Load(iniPath, modeFile);
        settings.Save(iniPath, modeFile);

        int pid = 0;
        IntPtr thread = IntPtr.Zero;
        try
        {
            (pid, thread) = GameLauncher.LaunchSuspended(
                binDir, options.User, GameLauncher.HexPass(authentication.Credential),
                options.ServerId);
            GameLauncher.Resume(thread);
            thread = IntPtr.Zero;
            uint processId = (uint)pid;
            new Thread(() => WindowMode.FrameGameWindow(
                processId, settings.DisplayMode, settings.ScreenWidth, settings.ScreenHeight))
            {
                IsBackground = true
            }.Start();
            WindowMode.Log($"puppet launch user='{options.User}' pid={pid}");
        }
        catch
        {
            if (pid != 0 && thread != IntPtr.Zero)
                GameLauncher.AbortSuspended(pid, thread);
            throw;
        }
    }
}

internal sealed record PuppetLaunchOptions(string User, string Password, string ServerId);
