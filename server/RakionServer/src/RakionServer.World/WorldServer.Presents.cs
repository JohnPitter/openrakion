using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public Task<PresentPeekResult> PeekPresentAsync(ClientSession session) =>
            session.GameInfoId > 0
                ? _db.PeekPresentAsync(session.GameInfoId)
                : Task.FromResult(new PresentPeekResult(PresentPeekStatus.Empty));

        public async Task<PresentAcceptResult> AcceptPresentAsync(
            ClientSession session, int pendingId, ushort slot)
        {
            if (session.GameInfoId <= 0 || slot >= System.Math.Min(120, session.BagCount * 30))
                return new PresentAcceptResult(PresentAcceptStatus.SlotOccupied);

            var gate = _characterLocks.GetOrAdd(
                session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var result = await session.AcceptPresentIntoStorageAsync(
                    slot, available => _db.AcceptPresentAsync(
                        session.GameInfoId, pendingId, slot, available));
                if (result.Status != PresentAcceptStatus.Success) return result;
                Log.Ok("present", "[{0}] aceitou pending={1}, item={2}, box={3}",
                    session.Slot, pendingId, result.ItemId, slot);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<PresentDisposeResult> DisposePresentAsync(
            ClientSession session, int pendingId)
        {
            if (session.GameInfoId <= 0)
                return new PresentDisposeResult(PresentDisposeStatus.Empty);

            var gate = _characterLocks.GetOrAdd(
                session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var result = await _db.DisposePresentAsync(session.GameInfoId, pendingId);
                if (result.Status == PresentDisposeStatus.Success)
                    Log.Ok("present", "[{0}] descartou pending={1}", session.Slot, pendingId);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public void NotifyRandomPresents(ClientSession owner, int[]? itemIds)
        {
            if (itemIds == null || itemIds.Length == 0) return;
            byte[] frame = LobbyFrames.PresentNotification(itemIds, owner.UserId);
            foreach (var session in Sessions)
                if (session.Connected) session.SendEncryptedFrame(frame);
            Log.Info("present", "[{0}] notificou {1} random present(s) da conta '{2}'",
                owner.Slot, itemIds.Length, owner.UserId);
        }
    }
}
