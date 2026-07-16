using System;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Entrypoint do Buddy Server. Portas padrao: 8500 (BuddyServer) e
    /// 8504 (BuddyCenter), conforme locale.ini do client (BuddyIP/BuddyPort).
    /// Override por args ou env RAKION_BUDDY_PORTS="8500,8504".
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Log.Info("boot", "================================================");
            Log.Info("boot", " Rakion Buddy Server (.NET) — reconstruido do DLL");
            Log.Info("boot", "================================================");

            string? iniPath = args.Length > 0 && args[0].EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                ? args[0]
                : Environment.GetEnvironmentVariable("RAKION_WORLD_INI");
            string[] portArguments = iniPath == null ? args : args[1..];
            BuddyConfig config = BuddyConfig.Load(iniPath, portArguments);
            var database = new BuddyDatabase(config.ConnectionString);
            try { await database.EnsureSchemaAsync(); }
            catch (Exception ex)
            {
                Log.Error("boot", "falha ao preparar banco Buddy: {0}", ex.Message);
                return 1;
            }
            var server = new BuddyServer(config, database);

            var stop = new CancellationTokenSource();
            void RequestStop() { try { if (!stop.IsCancellationRequested) stop.Cancel(); } catch (ObjectDisposedException) { } }
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; RequestStop(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestStop();

            try { server.Start(); }
            catch (Exception ex) { Log.Error("boot", "falha ao iniciar: {0}", ex.Message); return 1; }

            Log.Info("boot", "rodando — Ctrl+C para encerrar");
            try { await Task.Delay(Timeout.Infinite, stop.Token); }
            catch (OperationCanceledException) { }

            server.Stop();
            return 0;
        }
    }
}
