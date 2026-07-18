using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Security
{
    /// <summary>
    /// Sink de auditoria persistente: grava as violacoes na tabela <c>anticheat_log</c>
    /// (provisionada em <c>WorldDatabase.EnsureSchemaAsync</c>; o painel admin le).
    ///
    /// Nao-bloqueante (o INSERT e fire-and-forget — falha de DB nunca trava o dispatch) e
    /// AGREGADO por (slot, kind): dentro da janela de coalescencia as ocorrencias so
    /// incrementam um contador; a proxima linha sai com <c>hits</c> acumulado. Sem isso,
    /// um flood de pacotes viraria um flood identico de INSERTs (o proprio sink seria o DoS).
    /// Kick sempre grava na hora (evento raro e decisivo).
    /// </summary>
    public sealed class DbViolationSink : IViolationSink
    {
        private const int CoalesceMs = 5000;

        private readonly string _conn;
        private readonly Func<long> _nowMs;
        // Chave inclui a CONTA: slot e reusado entre sessoes — sem ela, o pending de uma sessao
        // antiga inflaria o `hits` atribuido a conta seguinte no mesmo slot dentro da janela.
        private readonly ConcurrentDictionary<(ushort Slot, string Account, ViolationKind Kind), Window> _windows = new();

        private sealed class Window
        {
            public long LastWriteMs;
            public int Pending;
        }

        public DbViolationSink(string connectionString, Func<long>? nowMs = null)
        {
            _conn = connectionString;
            _nowMs = nowMs ?? (() => Environment.TickCount64);
        }

        public void Record(in Violation v, int sessionScore, in GuardDecision decision)
        {
            var w = _windows.GetOrAdd((v.Slot, v.UserId ?? "", v.Kind), _ => new Window { LastWriteMs = long.MinValue });
            long now = _nowMs();
            int hits;
            lock (w)
            {
                if (!decision.Kick && now - w.LastWriteMs < CoalesceMs)
                {
                    w.Pending++;
                    return;
                }
                hits = w.Pending + 1;
                w.Pending = 0;
                w.LastWriteMs = now;
            }

            string action = decision.Kick ? "KICK" : decision.Drop ? "DROP" : "log";
            _ = InsertAsync(v, sessionScore, action, hits);
        }

        private async Task InsertAsync(Violation v, int score, string action, int hits)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "INSERT INTO anticheat_log (slot, account, kind, severity, score, action, hits, detail)" +
                    " VALUES (@slot, @account, @kind, @sev, @score, @action, @hits, @detail)", c);
                cmd.Parameters.AddWithValue("@slot", v.Slot);
                cmd.Parameters.AddWithValue("@account", Clamp(v.UserId, 32));
                cmd.Parameters.AddWithValue("@kind", v.Kind.ToString());
                cmd.Parameters.AddWithValue("@sev", (byte)v.Severity);
                cmd.Parameters.AddWithValue("@score", score);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@hits", hits);
                cmd.Parameters.AddWithValue("@detail", Clamp(v.Detail, 128));
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.Debug("guard", "anticheat_log insert falhou: {0}", ex.Message);
            }
        }

        private static string Clamp(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max);
    }
}
