using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using RakionServer.Common;

namespace RakionServer.World
{
    /// <summary>
    /// Modelo do worldserver.ini (mesmo formato do RakionWorldServ.exe nativo).
    /// Reconstruido 1:1 a partir do worldserver.ini do servidor v258.
    /// </summary>
    public sealed class WorldConfig
    {
        // [Server]
        public int ServerId = 1;
        public int MaxUser = 500;
        public int MaxField = 2000;
        public int Port = 40708;          // TCP do world

        // [UDP]
        public int UdpPort1 = 40708;
        public int UdpPort2 = 40709;
        public bool UdpRelayCompatibilityEnabled = true;
        public bool ForceTunneling;
        public int UdpRelayPacketsPerSecond = 300;
        public int UdpRelayBurst = 600;

        // [Broker]
        public string BrokerIp = "127.0.0.1";
        public int BrokerPort = 40706;
        public string BrokerCode = "";

        // [Authentication]
        public int AuthType = 0;          // 0 = sem auth.asp (login direto no DB)
        public string AuthHost = "127.0.0.1";
        public int AuthPort = 80;
        public string AuthPage = "/auth.asp";
        public bool AllowPasswordLogin = true;

        // [Client] — MD5 dos binarios do client que o world valida
        public bool ClientHashEnforced;
        private Domain.ClientHashSettings _clientHashes = new(false, "", "");
        public int RequiredClientAppId;
        public int RequiredClientBuildVersion;
        public Domain.ClientHashSettings ClientHashes => Volatile.Read(ref _clientHashes);
        public LauncherBuildIdentity? RequiredClientBuild => RequiredClientAppId > 0
            ? new(RequiredClientAppId, RequiredClientBuildVersion)
            : null;

        // [GM] — desligado por padrão; canal Special nunca concede authority.
        public bool GmEnabled;
        public readonly HashSet<string> GmOperationAllowedIps =
            new(StringComparer.OrdinalIgnoreCase);

        public ChatConfig Chat = new();
        public ClanConfig Clan = new();
        public CharacterDeleteConfig CharacterDelete = new();
        public BotEngineConfig BotEngine = new();

        public sealed class ChatConfig
        {
            public bool Enabled;
            public string AbuseFile = "abusestring.txt";
            public int Burst = 5;
            public int WindowSeconds = 5;
            public int RepeatLimit = 3;
            public int RepeatWindowSeconds = 10;
            public int AutoMuteSeconds = 30;
        }

        public sealed class ClanConfig
        {
            public bool Enabled;
            public int MaxMembers = 99;
            public int TreeMaxChildren = 7;
        }

        public sealed class CharacterDeleteConfig
        {
            public bool Enabled;
            public string Sender = "administrator@localhost";
            public string Subject = "Rakion";
            public string BodyFileName = "deletion.txt";
            public string PickupFolder = "";
            public string BaseDirectory = Environment.CurrentDirectory;
        }

        public sealed class BotEngineConfig
        {
            public bool Enabled;
            public string HostPath = "BotEngineHost.exe";
            public string ClientRoot = ".";
            public int StartupTimeoutSeconds = 30;
            public int ShutdownTimeoutSeconds = 5;
            public int MaxBotsPerField = 4;
        }

        // [DB] / [USERDB] / [LOGDB]
        public DbConfig Db = new();
        public DbConfig UserDb = new();
        public DbConfig LogDb = new();

        // [ServerList] — brokers que o world anuncia
        public readonly List<(string Ip, int Port)> Brokers = new();

        public sealed class DbConfig
        {
            public string Ip = "localhost";
            public int Port = 3306;
            public string User = "root";
            public string Pass = "123456";
            public string Name = "rakion";

            public string ConnectionString =>
                $"Server={Ip};Port={Port};Database={Name};Uid={User};Pwd={Pass};" +
                "Pooling=true;Default Command Timeout=15;Connection Timeout=10;" +
                "AllowPublicKeyRetrieval=true;SslMode=None;TreatTinyAsBoolean=false";
        }

        public static WorldConfig Load(string path)
        {
            var cfg = new WorldConfig();
            if (!File.Exists(path))
            {
                Log.Warn("config", "worldserver.ini nao encontrado em {0} — usando padroes", path);
                cfg.Brokers.Add((cfg.BrokerIp, cfg.BrokerPort));
                return cfg;
            }

            var ini = new IniFile(path);

            cfg.ServerId = ini.GetValue("Server", "ServerId", cfg.ServerId);
            cfg.MaxUser = ini.GetValue("Server", "MaxUser", cfg.MaxUser);
            cfg.MaxField = ini.GetValue("Server", "MaxField", cfg.MaxField);
            cfg.Port = ini.GetValue("Server", "Port", cfg.Port);

            cfg.UdpPort1 = ini.GetValue("UDP", "Port1", cfg.Port);
            cfg.UdpPort2 = ini.GetValue("UDP", "Port2", cfg.Port + 1);
            cfg.UdpRelayCompatibilityEnabled = ini.GetBool(
                "UDP", "RelayCompatibilityEnabled", cfg.UdpRelayCompatibilityEnabled);
            cfg.ForceTunneling = ini.GetBool("UDP", "ForceTunneling", cfg.ForceTunneling);
            cfg.UdpRelayPacketsPerSecond = Math.Max(1,
                ini.GetValue("UDP", "RelayPacketsPerSecond", cfg.UdpRelayPacketsPerSecond));
            cfg.UdpRelayBurst = Math.Max(cfg.UdpRelayPacketsPerSecond,
                ini.GetValue("UDP", "RelayBurst", cfg.UdpRelayBurst));

            cfg.BrokerIp = ini.GetValue("Broker", "IP", cfg.BrokerIp);
            cfg.BrokerPort = ini.GetValue("Broker", "Port", cfg.BrokerPort);
            cfg.BrokerCode = ini.GetValue("Broker", "Code", cfg.BrokerCode);

            cfg.AuthType = ini.GetValue("Authentication", "Type", cfg.AuthType);
            cfg.AuthHost = ini.GetValue("Authentication", "Host", cfg.AuthHost);
            cfg.AuthPort = ini.GetValue("Authentication", "Port", cfg.AuthPort);
            cfg.AuthPage = ini.GetValue("Authentication", "AuthPage", cfg.AuthPage);
            cfg.AllowPasswordLogin = ini.GetBool(
                "Authentication", "AllowPasswordLogin", cfg.AllowPasswordLogin);

            cfg.ClientHashEnforced = ini.GetValue(
                "Client", "EnforceMD5", cfg.ClientHashEnforced ? 1 : 0) != 0;
            cfg.UpdateClientHashes(
                ini.GetValue("Client", "MD5_1", "").Trim(),
                ini.GetValue("Client", "MD5_2", "").Trim());
            cfg.RequiredClientAppId = ini.GetValue(
                "Client", "RequiredAppId", cfg.RequiredClientAppId);
            cfg.RequiredClientBuildVersion = ini.GetValue(
                "Client", "RequiredBuildVersion", cfg.RequiredClientBuildVersion);
            ValidateClientHashes(cfg);

            cfg.GmEnabled = ini.GetValue("GM", "Enabled", cfg.GmEnabled ? 1 : 0) != 0;
            string allowedIps = ini.GetValue("GM", "AllowedIPs", "");
            foreach (string ip in allowedIps.Split(
                new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                cfg.GmOperationAllowedIps.Add(ip.Trim());

            cfg.Chat.Enabled = ini.GetBool("Chat", "Enabled", cfg.Chat.Enabled);
            cfg.Chat.AbuseFile = ResolveRelativePath(path,
                ini.GetValue("Chat", "AbuseFile", cfg.Chat.AbuseFile));
            cfg.Chat.Burst = Math.Clamp(
                ini.GetValue("Chat", "Burst", cfg.Chat.Burst), 1, 100);
            cfg.Chat.WindowSeconds = Math.Clamp(
                ini.GetValue("Chat", "WindowSeconds", cfg.Chat.WindowSeconds), 1, 300);
            cfg.Chat.RepeatLimit = Math.Clamp(
                ini.GetValue("Chat", "RepeatLimit", cfg.Chat.RepeatLimit), 2, 20);
            cfg.Chat.RepeatWindowSeconds = Math.Clamp(ini.GetValue(
                "Chat", "RepeatWindowSeconds", cfg.Chat.RepeatWindowSeconds), 1, 300);
            cfg.Chat.AutoMuteSeconds = Math.Clamp(
                ini.GetValue("Chat", "AutoMuteSeconds", cfg.Chat.AutoMuteSeconds), 1, 86400);

            cfg.Clan.Enabled = ini.GetBool("Clan", "Enabled", cfg.Clan.Enabled);
            cfg.Clan.MaxMembers = Math.Clamp(
                ini.GetValue("Clan", "MaxMembers", cfg.Clan.MaxMembers), 1, 99);
            cfg.Clan.TreeMaxChildren = Math.Clamp(
                ini.GetValue("Clan", "TreeMaxChildren", cfg.Clan.TreeMaxChildren), 1, 7);

            cfg.CharacterDelete.Enabled = ini.GetBool(
                "MailSender", "Enabled", cfg.CharacterDelete.Enabled);
            cfg.CharacterDelete.Sender = ini.GetValue(
                "MailSender", "Sender", cfg.CharacterDelete.Sender).Trim();
            string pickupFolder = ini.GetValue(
                "MailSender", "PickupFolder", cfg.CharacterDelete.PickupFolder).Trim();
            cfg.CharacterDelete.PickupFolder = pickupFolder.Length == 0
                ? "" : ResolveRelativePath(path, pickupFolder);
            cfg.CharacterDelete.Subject = ini.GetValue(
                "EMail", "CharacterDeleteSubject", cfg.CharacterDelete.Subject);
            cfg.CharacterDelete.BodyFileName = ini.GetValue(
                "EMail", "CharacterDeleteBodyFileName", cfg.CharacterDelete.BodyFileName);
            cfg.CharacterDelete.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
                ?? Environment.CurrentDirectory;

            cfg.BotEngine.Enabled = ini.GetBool(
                "BotEngine", "Enabled", cfg.BotEngine.Enabled);
            cfg.BotEngine.HostPath = ResolveRelativePath(path,
                ini.GetValue("BotEngine", "HostPath", cfg.BotEngine.HostPath));
            cfg.BotEngine.ClientRoot = ResolveRelativePath(path,
                ini.GetValue("BotEngine", "ClientRoot", cfg.BotEngine.ClientRoot));
            cfg.BotEngine.StartupTimeoutSeconds = Math.Clamp(ini.GetValue(
                "BotEngine", "StartupTimeoutSeconds",
                cfg.BotEngine.StartupTimeoutSeconds), 1, 120);
            cfg.BotEngine.ShutdownTimeoutSeconds = Math.Clamp(ini.GetValue(
                "BotEngine", "ShutdownTimeoutSeconds",
                cfg.BotEngine.ShutdownTimeoutSeconds), 1, 30);
            cfg.BotEngine.MaxBotsPerField = Math.Clamp(ini.GetValue(
                "BotEngine", "MaxBotsPerField",
                cfg.BotEngine.MaxBotsPerField), 1, 4);

            LoadDb(ini, "DB", cfg.Db);
            LoadDb(ini, "USERDB", cfg.UserDb);
            LoadDb(ini, "LOGDB", cfg.LogDb);

            int count = ini.GetValue("ServerList", "Count", 0);
            for (int i = 0; i < count; i++)
            {
                string ip = ini.GetValue("ServerList", "IP" + i, "");
                int port = ini.GetValue("ServerList", "Port" + i, cfg.BrokerPort);
                if (ip.Length > 0)
                    cfg.Brokers.Add((ip, port));
            }
            if (cfg.Brokers.Count == 0)
                cfg.Brokers.Add((cfg.BrokerIp, cfg.BrokerPort));

            return cfg;
        }

        private static string ResolveRelativePath(string iniPath, string value)
        {
            if (Path.IsPathRooted(value)) return value;
            string directory = Path.GetDirectoryName(Path.GetFullPath(iniPath))
                ?? Environment.CurrentDirectory;
            return Path.Combine(directory, value);
        }

        private static void ValidateClientHashes(WorldConfig cfg)
        {
            Domain.ClientHashSettings hashes = cfg.ClientHashes;
            bool hash1Valid = Domain.ClientHashPolicy.IsMd5(hashes.Md5_1);
            bool hash2Valid = Domain.ClientHashPolicy.IsMd5(hashes.Md5_2);
            if ((!string.IsNullOrEmpty(hashes.Md5_1) && !hash1Valid) ||
                (!string.IsNullOrEmpty(hashes.Md5_2) && !hash2Valid))
                throw new InvalidDataException("[Client] MD5_1/MD5_2 devem ter 32 dígitos hexadecimais");
            if (cfg.ClientHashEnforced && (!hash1Valid || !hash2Valid))
                throw new InvalidDataException("[Client] EnforceMD5=1 exige MD5_1 e MD5_2 válidos");
            if ((cfg.RequiredClientAppId == 0) != (cfg.RequiredClientBuildVersion == 0) ||
                cfg.RequiredClientAppId < 0 || cfg.RequiredClientBuildVersion < 0)
                throw new InvalidDataException(
                    "[Client] RequiredAppId e RequiredBuildVersion devem ser zero ou positivos juntos");
        }

        public void UpdateClientHashes(string md5_1, string md5_2) =>
            Volatile.Write(ref _clientHashes,
                new Domain.ClientHashSettings(ClientHashEnforced, md5_1, md5_2));

        private static void LoadDb(IniFile ini, string section, DbConfig db)
        {
            if (!ini.HasSection(section))
                return;
            db.Ip = ini.GetValue(section, "IP", db.Ip);
            db.Port = ini.GetValue(section, "Port", db.Port);
            db.User = ini.GetValue(section, "User", db.User);
            db.Pass = ini.GetValue(section, "Pass", db.Pass);
            db.Name = ini.GetValue(section, "Name", db.Name);
        }

        public void LogSummary()
        {
            Log.Info("config", "ServerId={0} MaxUser={1} MaxField={2} TCP={3} UDP={4}/{5}",
                ServerId, MaxUser, MaxField, Port, UdpPort1, UdpPort2);
            Log.Info("config", "Broker={0}:{1}  DB={2}@{3}:{4}/{5}  Auth.Type={6} GM={7}",
                BrokerIp, BrokerPort, Db.User, Db.Ip, Db.Port, Db.Name, AuthType,
                GmEnabled ? "on" : "off");
            Log.Info("config", "Chat moderation={0} burst={1}/{2}s abuse='{3}'",
                Chat.Enabled ? "on" : "off", Chat.Burst, Chat.WindowSeconds, Chat.AbuseFile);
            Log.Info("config", "Clan={0} members={1} treeChildren={2}",
                Clan.Enabled ? "on" : "off", Clan.MaxMembers, Clan.TreeMaxChildren);
            Log.Info("config", "Character delete pickup={0} folder='{1}'",
                CharacterDelete.Enabled ? "on" : "off", CharacterDelete.PickupFolder);
            Log.Info("config", "Bot Engine={0} maxBots={1}",
                BotEngine.Enabled ? "on" : "off", BotEngine.MaxBotsPerField);
        }
    }
}
