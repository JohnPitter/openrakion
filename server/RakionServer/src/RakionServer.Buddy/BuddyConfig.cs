using System;
using System.Collections.Generic;
using System.IO;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Resolve a connection string do banco `rakion` para o Buddy. O Buddy roda ao lado do World e compartilha
    /// o MESMO MariaDB (a identidade do messenger é por IP via <c>messenger_session</c>, gravada pelo World; a
    /// lista de amigos é a <c>buddylist</c>). Ordem de resolução:
    ///   1. env <c>RAKION_DB</c> (connection string completa) — deploy/docker pode injetar;
    ///   2. seção <c>[DB]</c> do <c>worldserver.ini</c> (env <c>RAKION_WORLD_INI</c> ou caminhos padrão) — fonte única com o World;
    ///   3. fallback localhost:3306/rakion (root) — mesmo default do World/Admin.
    /// </summary>
    public static class BuddyConfig
    {
        public static string ResolveConnectionString()
        {
            string? env = Environment.GetEnvironmentVariable("RAKION_DB");
            if (!string.IsNullOrWhiteSpace(env)) { Log.Ok("buddy", "DB via env RAKION_DB"); return env; }

            string? ini = FindWorldIni();
            if (ini != null)
            {
                var f = new IniFile(ini);
                if (f.HasSection("DB"))
                {
                    string ip = f.GetValue("DB", "IP", "127.0.0.1");
                    int port = f.GetValue("DB", "Port", 3306);
                    string user = f.GetValue("DB", "User", "root");
                    string pass = f.GetValue("DB", "Pass", "123456");
                    string name = f.GetValue("DB", "Name", "rakion");
                    Log.Ok("buddy", "DB do worldserver.ini: {0}@{1}:{2}/{3}", user, ip, port, name);
                    return RakionDb.BuildConnectionString(ip, port, name, user, pass);
                }
            }
            Log.Warn("buddy", "worldserver.ini [DB] não encontrado — DB em localhost:3306/rakion (root)");
            return RakionDb.BuildConnectionString("127.0.0.1", 3306, "rakion", "root", "123456");
        }

        /// <summary>Procura o worldserver.ini: env RAKION_WORLD_INI, /deploy (docker), ao lado do exe e
        /// deploy/worldserver.ini relativo à raiz do RakionServer (start-stack: sobe da bin do Buddy).</summary>
        private static string? FindWorldIni()
        {
            string baseDir = AppContext.BaseDirectory;
            var candidates = new List<string?>
            {
                Environment.GetEnvironmentVariable("RAKION_WORLD_INI"),
                "/deploy/worldserver.ini",
                Path.Combine(baseDir, "worldserver.ini"),
                // bin/Release/net9.0 -> sobe 5 níveis até a raiz do RakionServer, depois deploy/worldserver.ini
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "deploy", "worldserver.ini")),
            };
            foreach (var c in candidates)
                if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;
            return null;
        }
    }
}
