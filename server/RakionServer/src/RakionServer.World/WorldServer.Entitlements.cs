using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public async Task<EntitlementPurchaseResult> PurchaseEntitlementAsync(
            ClientSession session, InventoryEntitlement entitlement,
            byte paymentType, ushort paymentValue)
        {
            CharacterPayment? payment = BuildCharacterPayment(session, paymentType, paymentValue);
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0 || payment == null)
                return new EntitlementPurchaseResult(EntitlementPurchaseStatus.Failed);

            var gate = _characterLocks.GetOrAdd(session.GameInfoId,
                _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                EntitlementPurchaseResult result = await _db.PurchaseEntitlementAsync(
                    session.GameInfoId, session.UserId, session.ActiveCharId, entitlement, payment);
                if (result.Status != EntitlementPurchaseStatus.Success) return result;

                if (payment.UsesCoupon) session.ClearBoxCell(checked((byte)payment.Slot));
                session.Gold = checked((uint)result.Gold);
                session.Cash = checked((uint)result.Cash);
                if (entitlement == InventoryEntitlement.Bag) session.BagCount = result.Value;
                else session.CharacterSlotCount = result.Value;
                session.LoginCharList = await BuildLoginCharListAsync(session);
                NotifyRandomPresents(session, result.Presents);
                Log.Ok("shop", "[{0}] comprou {1}; valor={2} cash={3} pagamento={4}",
                    session.Slot, entitlement, result.Value, result.Cash,
                    payment.UsesCoupon ? $"coupon:{payment.ItemId}" : "cash");
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<PotionSlotPurchaseResult> PurchasePotionSlotAsync(ClientSession session)
        {
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0)
                return new PotionSlotPurchaseResult(EntitlementPurchaseStatus.Failed);
            var gate = _characterLocks.GetOrAdd(session.GameInfoId,
                _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                PotionSlotPurchaseResult result = await _db.PurchasePotionSlotAsync(
                    session.GameInfoId, session.UserId, session.ActiveCharId);
                if (result.Status != EntitlementPurchaseStatus.Success) return result;
                session.Gold = checked((uint)result.Gold);
                session.Cash = checked((uint)result.Cash);
                session.PotionSlotCount = result.PotionSlots;
                session.LoginCharList = await BuildLoginCharListAsync(session);
                NotifyRandomPresents(session, result.Presents);
                Log.Ok("shop", "[{0}] comprou potion slot; slots={1} gold={2} cash={3}",
                    session.Slot, result.PotionSlots, result.Gold, result.Cash);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<StageRankClearResult> PurchaseStageRankClearAsync(ClientSession session)
        {
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0)
                return new StageRankClearResult(StageEntitlementStatus.Failed);
            var gate = _characterLocks.GetOrAdd(session.GameInfoId,
                _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                StageRankClearResult result = await _db.PurchaseStageRankClearAsync(
                    session.GameInfoId, session.UserId, session.ActiveCharId);
                if (result.Status != StageEntitlementStatus.Success) return result;
                session.Gold = checked((uint)result.Gold);
                session.Cash = checked((uint)result.Cash);
                session.StageRanks = await _db.LoadStageRanksAsync(session.ActiveCharId);
                session.LoginCharList = await BuildLoginCharListAsync(session);
                NotifyRandomPresents(session, result.Presents);
                Log.Ok("shop", "[{0}] limpou ranks de stage do char={1}; cash={2}",
                    session.Slot, session.ActiveCharId, result.Cash);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<StageLevelFreeResult> PurchaseStageLevelFreeAsync(ClientSession session)
        {
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0)
                return new StageLevelFreeResult(StageEntitlementStatus.Failed);
            var gate = _characterLocks.GetOrAdd(session.GameInfoId,
                _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                StageLevelFreeResult result = await _db.PurchaseStageLevelFreeAsync(
                    session.GameInfoId, session.UserId, session.ActiveCharId);
                if (result.Status != StageEntitlementStatus.Success) return result;
                session.Gold = checked((uint)result.Gold);
                session.Cash = checked((uint)result.Cash);
                session.StageLevelFreeMarker = result.MinuteMarker;
                NotifyRandomPresents(session, result.Presents);
                Log.Ok("shop", "[{0}] comprou Stage Level Free; marcador={1} cash={2}",
                    session.Slot, result.MinuteMarker, result.Cash);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
