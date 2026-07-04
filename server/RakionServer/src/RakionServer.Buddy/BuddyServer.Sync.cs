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

        /// <summary>Re-lê a buddylist de UMA conexão, empurra os deltas E re-acende a lista (a UI do F9 descarta o
        /// RET_LOGIN/presença que chega ANTES da janela montar — a lista fica VAZIA até um evento forçar re-render;
        /// re-empurrar a presença a cada ciclo acende a lista sozinha em ~2.5s). O NTF_USER_STATE é idempotente.</summary>
        private async Task SyncOneAsync(BuddyConn conn)
        {
            var fresh = await _db.LoadBuddyListAsync(conn.Account);
            var freshNicks = new List<string>(fresh.Count);
            foreach (var b in fresh) freshNicks.Add(b.Nick);
            conn.BuddyNicks = freshNicks;
            if (freshNicks.Count == 0) return;

            // Re-acende a lista TODA: presença (online/offline + endereço P2P) de cada amigo. Idempotente — o
            // cliente só (re)ativa o P2P se ip1==ip2; offline zera. Isto conserta o "lista vazia até nick change".
            var roster = new List<UserPresence>(freshNicks.Count);
            foreach (var nick in freshNicks)
            {
                bool up = _byNick.TryGetValue(nick, out var f) && f!.LoggedIn && f != conn;
                roster.Add(up ? Presence(f!) : new UserPresence(nick, false, null, 0));
                if (up) Send(f!, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(new[] { Presence(conn) }));
            }
            Send(conn, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(roster));
        }
    }
}
