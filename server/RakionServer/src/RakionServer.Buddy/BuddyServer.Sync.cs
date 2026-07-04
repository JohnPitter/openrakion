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

        /// <summary>Re-lê a buddylist de UMA conexão e empurra os deltas vs o snapshot em memória.</summary>
        private async Task SyncOneAsync(BuddyConn conn)
        {
            var fresh = await _db.LoadBuddyListAsync(conn.Account);
            var freshNicks = new List<string>(fresh.Count);
            foreach (var b in fresh) freshNicks.Add(b.Nick);

            var old = new HashSet<string>(conn.BuddyNicks, StringComparer.OrdinalIgnoreCase);
            var added = new List<string>();
            foreach (var n in freshNicks) if (!old.Contains(n)) added.Add(n);
            var now = new HashSet<string>(freshNicks, StringComparer.OrdinalIgnoreCase);
            bool removed = false;
            foreach (var n in conn.BuddyNicks) if (!now.Contains(n)) { removed = true; break; }

            if (added.Count == 0 && !removed) return;   // sem mudança
            conn.BuddyNicks = freshNicks;

            // amigos NOVOS: manda a presença deles a ESTA conexão (acende a linha na lista, online/offline) e,
            // se o novo amigo está online, avisa-o que ESTA conexão está online (cross-announce, casado por nick).
            foreach (var nick in added)
            {
                bool up = _byNick.TryGetValue(nick, out var f) && f!.LoggedIn && f != conn;
                Send(conn, BuddyProtocol.NTF_USER_STATE,
                    BuddyFrames.UserState(new[] { up ? Presence(f!) : new UserPresence(nick, false, null, 0) }));
                if (up)
                    Send(f!, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(new[] { Presence(conn) }));
            }
            Log.Ok("buddy", "[{0}] sync '{1}': +{2} amigo(s){3} -> presença empurrada",
                conn.Ip, conn.Account, added.Count, removed ? " (e remoções)" : "");
        }
    }
}
