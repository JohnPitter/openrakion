using System;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World;
using RakionServer.World.Database;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Sobe um <see cref="WorldServer"/> REAL em loopback (TCP + UDP + motor da partida)
    /// contra o MySQL/MariaDB de teste, para a validação dinâmica headless. Portas
    /// próprias (não colidem com o stack de produção 40708/9).
    ///
    /// Gate: a suíte só roda com o banco acessível. A connection string vem de
    /// RAKION_E2E_CONNECTION; sem ela, tenta o padrão local do stack de dev
    /// (root/123456 @ localhost:3306, base `rakion`). <see cref="Available"/> reflete
    /// se o Ping passou — os testes fazem skip suave quando indisponível, como os
    /// *DatabaseSmokeTests já existentes.
    /// </summary>
    public sealed class WorldServerFixture : IAsyncDisposable
    {
        public const string Host = "127.0.0.1";

        // Cada fixture usa um trio de portas próprio para não colidir com a instância
        // anterior (o socket pode ficar em TIME_WAIT entre testes serializados).
        private static int _portCursor = 41700;
        public int TcpPort { get; private set; }
        public int UdpPort2 { get; private set; }
        public int BrokerPort { get; private set; }

        public WorldServer? Server { get; private set; }
        public bool Available { get; private set; }
        public string Reason { get; private set; } = "";
        public string DbConnectionString { get; private set; } = "";

        private WorldServerFixture() { }

        public static async Task<WorldServerFixture> CreateAsync(
            bool forceTunneling = false,
            Action<WorldConfig>? configure = null)
        {
            int basePort = Interlocked.Add(ref _portCursor, 10);
            var fixture = new WorldServerFixture
            {
                TcpPort = basePort,
                UdpPort2 = basePort + 1,
                BrokerPort = basePort + 2,
            };
            string conn = Environment.GetEnvironmentVariable("RAKION_E2E_CONNECTION")
                ?? "Server=127.0.0.1;Port=3306;Database=rakion;Uid=root;Pwd=123456;" +
                   "Pooling=true;Default Command Timeout=15;Connection Timeout=5;" +
                   "AllowPublicKeyRetrieval=true;SslMode=None;TreatTinyAsBoolean=false";

            var csb = new MySqlConnectionStringBuilder(conn);
            var cfg = new WorldConfig
            {
                Port = fixture.TcpPort,
                UdpPort1 = fixture.TcpPort,
                UdpPort2 = fixture.UdpPort2,
                BrokerPort = fixture.BrokerPort,
                AuthType = 0,
                AllowPasswordLogin = true,
                MaxUser = 64,
                ForceTunneling = forceTunneling,
            };
            cfg.Db.Ip = csb.Server;
            cfg.Db.Port = (int)csb.Port;
            cfg.Db.User = csb.UserID;
            cfg.Db.Pass = csb.Password;
            cfg.Db.Name = string.IsNullOrEmpty(csb.Database) ? "rakion" : csb.Database;
            configure?.Invoke(cfg);

            fixture.DbConnectionString = cfg.Db.ConnectionString;
            var db = new WorldDatabase(cfg.Db);
            try
            {
                if (!await db.PingAsync())
                {
                    fixture.Reason = "PingAsync=false";
                    ThrowWhenRequired(fixture.Reason);
                    return fixture;
                }
            }
            catch (Exception ex)
            {
                fixture.Reason = "DB inacessível: " + ex.Message;
                ThrowWhenRequired(fixture.Reason);
                return fixture;
            }

            var server = new WorldServer(cfg, db);
            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                fixture.Reason = "StartAsync falhou: " + ex.Message;
                try { server.Stop(); } catch { }
                ThrowWhenRequired(fixture.Reason);
                return fixture;
            }

            fixture.Server = server;
            fixture.Available = true;
            return fixture;
        }

        private static void ThrowWhenRequired(string reason)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("RAKION_E2E_REQUIRED"),
                    "1", StringComparison.Ordinal))
                throw new InvalidOperationException("E2E World obrigatório indisponível: " + reason);
        }

        public ValueTask DisposeAsync()
        {
            try { Server?.Stop(); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
