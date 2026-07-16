using System.Collections.Generic;
using System.Threading.Tasks;
using RakionServer.World.Database;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        public async Task ApplyOnlineInventoryRefreshAsync(
            IReadOnlyList<UserItem> activeItems, IReadOnlyList<StorageItem> storageItems,
            IReadOnlyList<(int Cell, int ItemId, int Count)> quickslot)
        {
            await _storageMutationLock.WaitAsync();
            try
            {
                int[] oldBoxItems = BoxItems.ToArray();
                int[] oldBoxCounts = BoxCount.ToArray();
                int[] oldActiveItems = (int[])_potionSlot.Clone();
                int[] oldActiveCounts = (int[])_potionCount.Clone();

                Items = new List<UserItem>(activeItems);
                SetBoxItems(storageItems);
                LoadActiveItems(activeItems);
                LoadPotionSlot(quickslot);
                PublishBoxExpirationDelta(oldBoxItems, oldBoxCounts);
                PublishActiveExpirationDelta(oldActiveItems, oldActiveCounts);
            }
            finally
            {
                _storageMutationLock.Release();
            }
        }

        private void PublishBoxExpirationDelta(int[] oldItems, int[] oldCounts)
        {
            int count = System.Math.Min(oldItems.Length, BoxItems.Count);
            for (int cell = 0; cell < count; cell++)
            {
                if (oldItems[cell] == BoxItems[cell] && oldCounts[cell] == BoxCount[cell]) continue;
                SendBoxAdd(BoxItems[cell], checked((byte)cell),
                    checked((byte)(1 + BoxLevel[cell])), BoxCount[cell]);
            }
        }

        private void PublishActiveExpirationDelta(int[] oldItems, int[] oldCounts)
        {
            int count = System.Math.Min(oldItems.Length, _potionSlot.Length);
            for (byte cell = 0; cell < count; cell++)
            {
                if (oldItems[cell] == _potionSlot[cell] &&
                    oldCounts[cell] == _potionCount[cell]) continue;
                if (_potionSlot[cell] != 0)
                    SendPotionSlotAdd(_potionSlot[cell], cell, _potionCount[cell]);
                else
                    SendActiveSlotClear(cell);
            }
        }

        private void SendActiveSlotClear(byte cell)
        {
            int emptyBoxCell = BoxItems.IndexOf(0);
            SendEncryptedFrame(InventoryExpirationFrames.ActiveSlotClear(cell, emptyBoxCell));
        }
    }
}
