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

        /// <summary>Re-lê a buddylist de UMA conexão e empurra SÓ os deltas (amigo NOVO adicionado no World via
        /// 0x19). O add não passa pelo Buddy, então sem isto o amigo novo só aparecia no outro cliente após relog.
        /// NÃO re-empurra a lista inteira a cada ciclo — re-enviar presença OFFLINE repetida limpava a lista no
        /// cliente (a UI trata offline como remoção). A lista inicial vem do RET_LOGIN + AnnounceOnline no login.</summary>
        private async Task SyncOneAsync(BuddyConn conn)
        {
            var fresh = await _db.LoadBuddyListAsync(conn.Account);
            var freshNicks = new List<string>(fresh.Count);
            foreach (var b in fresh) freshNicks.Add(b.Nick);

            var old = new HashSet<string>(conn.BuddyNicks, StringComparer.OrdinalIgnoreCase);
            var added = new List<string>();
            foreach (var n in freshNicks) if (!old.Contains(n)) added.Add(n);
            if (added.Count == 0) { conn.BuddyNicks = freshNicks; return; }   // sem amigo novo
            conn.BuddyNicks = freshNicks;

            // amigo NOVO: presença dele a ESTA conexão (acende a linha) + cross-announce se ele está online.
            foreach (var nick in added)
            {
                bool up = _byNick.TryGetValue(nick, out var f) && f!.LoggedIn && f != conn;
                Send(conn, BuddyProtocol.NTF_USER_STATE,
                    BuddyFrames.UserState(new[] { up ? Presence(f!) : new UserPresence(nick, false, null, 0) }));
                if (up) Send(f!, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(new[] { Presence(conn) }));
            }
            Log.Ok("buddy", "[{0}] sync '{1}': +{2} amigo(s) novo(s) -> presença empurrada", conn.Ip, conn.Account, added.Count);
        }
    }
}
