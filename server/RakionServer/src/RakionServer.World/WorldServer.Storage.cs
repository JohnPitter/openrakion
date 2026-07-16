using System.Collections.Generic;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public async Task<StoragePurchaseResult> PurchaseStorageAsync(
            ClientSession session, StoragePurchaseIntent intent)
        {
            CharacterPayment? payment = BuildCharacterPayment(
                session, intent.PaymentType, intent.PaymentValue);
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0 ||
                string.IsNullOrEmpty(session.UserId) || payment == null)
                return new StoragePurchaseResult(StorageMutationStatus.Failed);
            var gate = _characterLocks.GetOrAdd(
                session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                IReadOnlyList<int> itemIds = ExpandSetMembers(intent.ItemId);
                if (itemIds.Count == 0) itemIds = new[] { intent.ItemId };
                StorageGrant[]? grants = PlanStorageGrants(session, itemIds);
                if (grants == null)
                    return new StoragePurchaseResult(StorageMutationStatus.NoSpace);
                var result = await _db.PurchaseStorageAsync(new StoragePurchaseCommand(
                    session.GameInfoId, session.UserId, session.ActiveCharId, session.CharClass,
                    intent.ItemId, intent.PayGold, intent.BasePrice, StorageCapacity(session),
                    payment, grants));
                if (result.Status != StorageMutationStatus.Success) return result;
                for (int i = 0; i < grants.Length; i++)
                    session.ApplyStorageGrant(
                        grants[i].ItemId, grants[i].Cell!.Value, 0, result.RowIds![i]);
                if (payment.UsesCoupon) session.ClearBoxCell(checked((byte)payment.Slot));
                if (intent.PayGold) session.Gold = checked((uint)result.Balance);
                else session.Cash = checked((uint)result.Balance);
                NotifyRandomPresents(session, result.Presents);
                Log.Ok("shop", "[{0}] compra commitada item={1}, grants={2}, {3}={4}",
                    session.Slot, intent.ItemId, grants.Length,
                    intent.PayGold ? "gold" : "cash", result.Balance);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<StorageSaleResult> SellStorageAsync(ClientSession session, byte slot)
        {
            if (session.GameInfoId <= 0 || slot >= session.BoxItems.Count)
                return new StorageSaleResult(StorageMutationStatus.Failed);
            int itemId = session.BoxItems[slot];
            int rowId = session.BoxRowId[slot];
            if (itemId == 0 || rowId <= 0)
                return new StorageSaleResult(StorageMutationStatus.Failed);
            int price = StorageEconomyRules.SellPrice(FindItemDef(itemId), itemId);
            bool stack = StorageEconomyRules.IsPotion(itemId);

            var gate = _characterLocks.GetOrAdd(
                session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var result = await _db.SellStorageAsync(new StorageSaleCommand(
                    session.GameInfoId, session.ActiveCharId, rowId, itemId, slot, stack, price));
                if (result.Status != StorageMutationStatus.Success) return result;
                session.ClearBoxCell(slot);
                session.Gold = checked((uint)result.Gold);
                Log.Ok("shop", "[{0}] venda commitada slot={1}, item={2}, gold+={3}",
                    session.Slot, slot, itemId, price);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        private StorageGrant[]? PlanStorageGrants(
            ClientSession session, IReadOnlyList<int> itemIds)
        {
            bool[] occupied = new bool[StorageCapacity(session)];
            for (int i = 0; i < occupied.Length; i++) occupied[i] = session.BoxItems[i] != 0;
            var plannedPotions = new Dictionary<int, int>();
            var grants = new StorageGrant[itemIds.Count];
            for (int i = 0; i < itemIds.Count; i++)
            {
                int item = itemIds[i];
                int cell = -1;
                if (StorageEconomyRules.IsPotion(item))
                {
                    cell = session.BoxItems.IndexOf(item);
                    if (cell < 0 && !plannedPotions.TryGetValue(item, out cell)) cell = -1;
                }
                if (cell < 0)
                {
                    cell = System.Array.FindIndex(occupied, used => !used);
                    if (cell < 0) return null;
                    occupied[cell] = true;
                    if (StorageEconomyRules.IsPotion(item)) plannedPotions[item] = cell;
                }
                grants[i] = new StorageGrant(item, cell);
            }
            return grants;
        }

        private static int StorageCapacity(ClientSession session) =>
            System.Math.Clamp(session.BagCount * 30, 30, 120);
    }
}
