using System.Collections.Generic;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        private async Task<InventoryHydration> HydrateInventoryAsync(
            ClientSession session, CharacterInfo character, string flow)
        {
            List<UserItem> activeItems = await _db.LoadItemsAsync(character.Id);
            var invalidItems = activeItems.FindAll(item => !IsActiveItemValid(character, item));
            if (invalidItems.Count > 0)
            {
                int moved = await _db.MoveInvalidItemsToStorageAsync(
                    session.GameInfoId, character.Id,
                    invalidItems.ConvertAll(item => item.Id), StorageCapacity(session));
                if (moved < 0)
                    Log.Error(flow, "[{0}] equipamento incompatível não pôde ser normalizado", session.Slot);
                else
                {
                    Log.Warn(flow, "[{0}] {1} item(ns) incompatível(is) movido(s) ao storage",
                        session.Slot, moved);
                    activeItems = await _db.LoadItemsAsync(character.Id);
                }
            }

            List<StorageItem> storageItems = await LoadNormalizedStorageAsync(session, flow);
            session.Items = activeItems;
            session.SetBoxItems(storageItems);
            session.LoadActiveItems(activeItems);
            session.LoadPotionSlot(await _db.LoadQuickslotAsync(session.GameInfoId, character.Id));
            if (!await session.PersistStorageLayoutAsync(character.Id))
                Log.Warn(flow, "[{0}] layout do armazém não pôde ser normalizado", session.Slot);

            int visibleItems = storageItems.FindAll(item => IsBoxDisplayable(item.ItemId)).Count;
            return new InventoryHydration(activeItems.Count, visibleItems,
                storageItems.Count - visibleItems);
        }

        private async Task<List<StorageItem>> LoadNormalizedStorageAsync(
            ClientSession session, string flow)
        {
            List<StorageItem> storageItems = await _db.LoadStorageItemsAsync(session.GameInfoId);
            var sets = storageItems.FindAll(item => IsSet(item.ItemId));
            if (sets.Count == 0) return storageItems;

            foreach (StorageItem set in sets)
                await _db.UnpackSetInStorageAsync(
                    session.GameInfoId, set, ExpandSetMembers(set.ItemId), StorageCapacity(session));
            Log.Ok(flow, "[{0}] {1} set(s) legado(s) type-10 processado(s) no armazém",
                session.Slot, sets.Count);
            return await _db.LoadStorageItemsAsync(session.GameInfoId);
        }

        private readonly record struct InventoryHydration(
            int ActiveItems, int VisibleStorageItems, int HiddenStorageItems);
    }
}
