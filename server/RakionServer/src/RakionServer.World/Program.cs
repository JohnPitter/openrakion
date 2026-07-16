using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.World
{
    /// <summary>
    /// Entrypoint do World Server reconstruido. Le worldserver.ini (mesmo formato
    /// do RakionWorldServ.exe), sobe TCP/UDP do jogo + canal IPC com o broker.
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Log.Info("boot", "==================================================");
            Log.Info("boot", " Rakion World Server (.NET) — reconstruido do exe");
            Log.Info("boot", "==================================================");

            string iniPath = ResolveIniPath(args);
            Log.Info("boot", "config: {0}", iniPath);

            WorldConfig cfg = WorldConfig.Load(iniPath);
            cfg.LogSummary();

            // auto-teste da cifra de pacote (AES-128, chave/IV do worldserv.exe)
            if (RakionServer.Common.PacketCrypto.SelfTest())
                Log.Ok("crypto", "AES self-test OK (chave/IV do worldserv.exe, canal lobby cifrado)");
            else
                Log.Error("crypto", "AES self-test FALHOU — cifra inconsistente");

            var db = new Database.WorldDatabase(cfg.Db, cfg.UserDb);
            var server = new WorldServer(cfg, db);

            var stop = new CancellationTokenSource();
            void RequestStop() { try { if (!stop.IsCancellationRequested) stop.Cancel(); } catch (ObjectDisposedException) { } }
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; RequestStop(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestStop();

            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Error("boot", "falha ao iniciar: {0}", ex);
                return 1;
            }

            Log.Info("boot", "rodando — Ctrl+C para encerrar");
            try { await Task.Delay(Timeout.Infinite, stop.Token); }
            catch (OperationCanceledException) { }

            Log.Info("boot", "encerrando...");
            server.Stop();
            return 0;
        }

        private static string ResolveIniPath(string[] args)
        {
            if (args.Length > 0 && args[0].Length > 0)
                return args[0];
            string env = Environment.GetEnvironmentVariable("RAKION_WORLD_INI") ?? "";
            if (env.Length > 0)
                return env;
            string local = Path.Combine(Environment.CurrentDirectory, "worldserver.ini");
            return local;
        }
    }
}
