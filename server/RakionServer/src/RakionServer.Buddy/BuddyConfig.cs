using System;
using System.IO;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    public sealed class BuddyConfig
    {
        public int[] Ports { get; init; } = [8500, 8504];
        public string ConnectionString { get; init; } = "";
        public ChatModerationSettings Moderation { get; init; } = new(
            true, 5, TimeSpan.FromSeconds(5), 3,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        public string AbuseFile { get; init; } = "abusestring.txt";

        public static BuddyConfig Load(string? iniPath, string[] portArguments)
        {
            IniFile ini = new(iniPath ?? "");
            string connection = Environment.GetEnvironmentVariable("ConnectionStrings__Rakion") ??
                BuildConnectionString(ini);
            string abuseFile = ResolveFile(iniPath,
                ini.GetValue("Chat", "AbuseFile", "abusestring.txt"));
            return new BuddyConfig
            {
                Ports = ParsePorts(portArguments),
                ConnectionString = connection,
                AbuseFile = abuseFile,
                Moderation = new ChatModerationSettings(
                    ini.GetBool("Chat", "Enabled", true),
                    Math.Max(1, ini.GetValue("Chat", "Burst", 5)),
                    TimeSpan.FromSeconds(Math.Max(1, ini.GetValue("Chat", "WindowSeconds", 5))),
                    Math.Max(2, ini.GetValue("Chat", "RepeatLimit", 3)),
                    TimeSpan.FromSeconds(Math.Max(1, ini.GetValue("Chat", "RepeatWindowSeconds", 10))),
                    TimeSpan.FromSeconds(Math.Max(1, ini.GetValue("Chat", "AutoMuteSeconds", 30))))
            };
        }

        private static string BuildConnectionString(IniFile ini)
        {
            string host = ini.GetValue("DB", "IP", "127.0.0.1");
            int port = ini.GetValue("DB", "Port", 3306);
            string user = ini.GetValue("DB", "User", "root");
            string pass = ini.GetValue("DB", "Pass", "");
            string name = ini.GetValue("DB", "Name", "rakion");
            return $"Server={host};Port={port};Database={name};Uid={user};Pwd={pass};" +
                "SslMode=None;AllowPublicKeyRetrieval=True;CharacterSet=utf8mb4";
        }

        private static int[] ParsePorts(string[] arguments)
        {
            string spec = arguments.Length > 0 ? string.Join(',', arguments) :
                Environment.GetEnvironmentVariable("RAKION_BUDDY_PORTS") ?? "8500,8504";
            var ports = new System.Collections.Generic.List<int>();
            foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out int port) && port is > 0 and <= 65535)
                    ports.Add(port);
            return ports.Count == 0 ? [8500, 8504] : ports.ToArray();
        }

        private static string ResolveFile(string? iniPath, string value)
        {
            if (Path.IsPathRooted(value) || string.IsNullOrEmpty(iniPath)) return value;
            string? directory = Path.GetDirectoryName(Path.GetFullPath(iniPath));
            return directory == null ? value : Path.Combine(directory, value);
        }
    }
}
