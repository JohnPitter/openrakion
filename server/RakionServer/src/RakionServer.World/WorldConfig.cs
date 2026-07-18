using System;
using System.Collections.Generic;
using System.IO;
using RakionServer.Common;
using RakionServer.World.Security;

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

        // [Broker]
        public string BrokerIp = "127.0.0.1";
        public int BrokerPort = 40706;

        // [Authentication]
        public int AuthType = 0;          // 0 = sem auth.asp (login direto no DB)
        public string AuthHost = "127.0.0.1";
        public int AuthPort = 80;
        public string AuthPage = "/auth.asp";

        // [Client] — MD5 dos binarios do client que o world valida
        public string ClientMd5_1 = "";
        public string ClientMd5_2 = "";

        // [AntiCheat] — anti-cheat server-side (OpenGuard)
        public AntiCheatConfig AntiCheat = new();

        // [DB] / [USERDB] / [LOGDB]
        public DbConfig Db = new();
        public DbConfig UserDb = new();
        public DbConfig LogDb = new();

        // [ServerList] — brokers que o world anuncia
        public readonly List<(string Ip, int Port)> Brokers = new();

        // Estagios de tutorial liberados (opcode 0x65). Vazio = nenhum restrito.
        public int[] AllowedTutorialStages = System.Array.Empty<int>();

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
                "AllowPublicKeyRetrieval=true;SslMode=None";
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

            cfg.BrokerIp = ini.GetValue("Broker", "IP", cfg.BrokerIp);
            cfg.BrokerPort = ini.GetValue("Broker", "Port", cfg.BrokerPort);

            cfg.AuthType = ini.GetValue("Authentication", "Type", cfg.AuthType);
            cfg.AuthHost = ini.GetValue("Authentication", "Host", cfg.AuthHost);
            cfg.AuthPort = ini.GetValue("Authentication", "Port", cfg.AuthPort);
            cfg.AuthPage = ini.GetValue("Authentication", "AuthPage", cfg.AuthPage);

            cfg.ClientMd5_1 = ini.GetValue("Client", "MD5_1", cfg.ClientMd5_1);
            cfg.ClientMd5_2 = ini.GetValue("Client", "MD5_2", cfg.ClientMd5_2);

            LoadAntiCheat(ini, cfg.AntiCheat);

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

        private static void LoadAntiCheat(IniFile ini, AntiCheatConfig ac)
        {
            const string s = "AntiCheat";
            if (!ini.HasSection(s))
                return;
            ac.Enabled = ini.GetBool(s, "Enabled", ac.Enabled);
            ac.EnforceKick = ini.GetBool(s, "EnforceKick", ac.EnforceKick);
            ac.EnforceClientHash = ini.GetBool(s, "EnforceClientHash", ac.EnforceClientHash);
            ac.OpcodeWindowMs = ini.GetValue(s, "OpcodeWindowMs", ac.OpcodeWindowMs);
            ac.MaxOpcodesPerWindow = ini.GetValue(s, "MaxOpcodesPerWindow", ac.MaxOpcodesPerWindow);
            ac.GameplayWindowMs = ini.GetValue(s, "GameplayWindowMs", ac.GameplayWindowMs);
            ac.MaxGameplayPerWindow = ini.GetValue(s, "MaxGameplayPerWindow", ac.MaxGameplayPerWindow);
            ac.KickScore = ini.GetValue(s, "KickScore", ac.KickScore);
        }

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
            Log.Info("config", "Broker={0}:{1}  DB={2}@{3}:{4}/{5}  Auth.Type={6}",
                BrokerIp, BrokerPort, Db.User, Db.Ip, Db.Port, Db.Name, AuthType);
            Log.Info("config", "OpenGuard: enabled={0} enforceKick={1} enforceHash={2} (kick@{3})",
                AntiCheat.Enabled, AntiCheat.EnforceKick, AntiCheat.EnforceClientHash, AntiCheat.KickScore);
        }
    }
}
