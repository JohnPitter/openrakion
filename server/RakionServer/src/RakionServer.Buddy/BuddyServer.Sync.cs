using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Sincronização VIVA da buddylist (messenger F9). O ADD de amigo nasce no WORLD (handler 0x19 -> DB
    /// buddylist recíproca) e NÃO passa pelo Buddy — então a lista em memória de cada conexão (carregada no
    /// login) fica ESTÁTICA e o novo amigo só aparecia no OUTRO cliente após relog. Aqui o Buddy re-lê a
    /// buddylist do DB periodicamente e empurra os deltas: amigo NOVO -> NTF_USER_STATE (acende na lista) +
    /// cross-announce de presença se ambos online; amigo REMOVIDO -> tira da presença. Sem canal World->Buddy
    /// (só o DB compartilhado), este poll é a ponte.
    /// </summary>
    public sealed partial class BuddyServer
    {
        private const int SyncIntervalMs = 2500;   // latência aceitável p/ o amigo novo aparecer (~2.5s)

        private void StartBuddyListSync()
        {
            _ = Task.Run(() => SyncLoopAsync(_cts.Token));
        }

        private async Task SyncLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(SyncIntervalMs, ct); }
                catch (OperationCanceledException) { break; }
                foreach (var conn in _byAccount.Values)
                {
                    if (!conn.LoggedIn) continue;
                    try { await SyncOneAsync(conn); }
                    catch (Exception ex) { Log.Debug("buddy", "sync {0}: {1}", conn.Account, ex.Message); }
                }
            }
        }

        /// <summary>Re-lê a buddylist de UMA conexão; se a ASSINATURA (conta:nick) MUDOU — add (World 0x19),
        /// remove (Buddy 0x3002) ou nick change (World 0x15) — re-emite o RET_LOGIN COMPLETO + presença, como no
        /// login. O add aparece na hora (find-or-insert por nick). Só dispara na MUDANÇA (não a cada ciclo) p/ não
        /// re-emitir presença OFFLINE repetida (que desregistra a linha). O add não passa pelo Buddy (nasce no
        /// World -> DB), então este poll da buddylist é a ponte World->Buddy.</summary>
        private async Task SyncOneAsync(BuddyConn conn)
        {
            var fresh = await _db.LoadBuddyListAsync(conn.Account);
            string sig = BuddySig(fresh);
            if (sig == conn.BuddyListSig) return;   // lista inalterada

            conn.BuddyListSig = sig;
            conn.BuddyNicks = fresh.ConvertAll(b => b.Nick);
            Send(conn, BuddyProtocol.RET_LOGIN, BuddyFrames.LoginList(conn.Token, fresh));   // re-monta a lista
            AnnounceOnline(conn);                                                            // acende online/offline
            Log.Ok("buddy", "[{0}] sync '{1}': lista mudou -> RET_LOGIN re-emitido ({2} amigo(s))",
                conn.Ip, conn.Account, fresh.Count);
        }

        /// <summary>Assinatura estável da buddylist (conta:nick ordenado) — muda quando um amigo é adicionado,
        /// removido ou tem o nick trocado, disparando a re-emissão da lista à conexão afetada.</summary>
        private static string BuddySig(List<BuddyEntry> list)
        {
            var parts = new List<string>(list.Count);
            foreach (var b in list) parts.Add(b.Account + ":" + b.Nick);
            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", parts);
        }
    }
}
