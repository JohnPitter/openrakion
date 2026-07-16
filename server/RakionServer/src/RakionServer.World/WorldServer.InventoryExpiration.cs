using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        private async Task InventoryExpirationLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    await PurgeOnlineInventoryAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Warn("inventory", "expiração online: {0}", ex.Message);
                }
            }
        }

        private async Task PurgeOnlineInventoryAsync()
        {
            var accounts = new Dictionary<int, List<ClientSession>>();
            foreach (ClientSession session in Sessions)
            {
                if (!session.Connected || session.GameInfoId <= 0 || session.ActiveCharId <= 0)
                    continue;
                if (!accounts.TryGetValue(session.GameInfoId, out List<ClientSession>? sessions))
                    accounts[session.GameInfoId] = sessions = [];
                sessions.Add(session);
            }

            foreach ((int userId, List<ClientSession> sessions) in accounts)
                await PurgeOnlineAccountAsync(userId, sessions);
        }

        private async Task PurgeOnlineAccountAsync(int userId, List<ClientSession> sessions)
        {
            var gate = _characterLocks.GetOrAdd(
                userId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                int removed = await _db.PurgeExpiredInventoryAsync(userId);
                if (removed <= 0) return;
                IReadOnlyList<Database.StorageItem> storage =
                    await _db.LoadStorageItemsAsync(userId);
                foreach (ClientSession session in sessions)
                {
                    IReadOnlyList<Database.UserItem> active =
                        await _db.LoadItemsAsync(session.ActiveCharId);
                    var quickslot = await _db.LoadQuickslotAsync(userId, session.ActiveCharId);
                    await session.ApplyOnlineInventoryRefreshAsync(active, storage, quickslot);
                    session.LoginCharList = await BuildLoginCharListAsync(session);
                }
                Log.Info("inventory", "expiração online atualizou {0} sessão(ões) do user {1}",
                    sessions.Count, userId);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
