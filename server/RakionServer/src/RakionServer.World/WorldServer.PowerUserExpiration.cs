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
        private async Task PowerUserExpirationLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    await RefreshOnlinePowerUsersAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Warn("shop", "validade online do Power User: {0}", ex.Message);
                }
            }
        }

        private async Task RefreshOnlinePowerUsersAsync()
        {
            var accounts = new Dictionary<int, List<ClientSession>>();
            foreach (ClientSession session in Sessions)
            {
                if (!session.Connected || session.GameInfoId <= 0) continue;
                if (!accounts.TryGetValue(session.GameInfoId, out List<ClientSession>? sessions))
                    accounts[session.GameInfoId] = sessions = [];
                sessions.Add(session);
            }
            foreach ((int userId, List<ClientSession> sessions) in accounts)
                await RefreshOnlinePowerUserAsync(userId, sessions);
        }

        private async Task RefreshOnlinePowerUserAsync(
            int userId, IReadOnlyList<ClientSession> sessions)
        {
            var gate = _characterLocks.GetOrAdd(
                userId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                DateTime? expiration = await _db.LoadPowerUserExpirationAsync(userId);
                DateTime now = DateTime.Now;
                foreach (ClientSession session in sessions)
                    session.RefreshPowerUserExpiration(expiration, now);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
