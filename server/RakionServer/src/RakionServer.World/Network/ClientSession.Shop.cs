using System;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        private async Task HandleStoragePurchaseAsync(byte[] data)
        {
            if (!InventoryBuyRequest.TryParse(data, out var request))
            {
                Disconnect(0x37);
                return;
            }
            var item = _server.FindItemDef(request.ItemId);
            if (item == null) { Disconnect(0x37); return; }
            byte uiStatus = _inventoryUiState.MutationStatus(InventoryMutationInProgress);
            if (uiStatus != 0) { SendStorageError(uiStatus); return; }
            if (!TryStartInventoryMutation()) { SendStorageError(2); return; }

            ShopBuyInProgress = true;
            try
            {
                int? quotedPrice = StorageEconomyRules.PurchasePrice(item, request.PaysGold);
                if (quotedPrice == null) { SendStorageError(3); return; }
                StoragePurchaseResult result = await _server.PurchaseStorageAsync(
                    this, new StoragePurchaseIntent(
                        request.ItemId, request.PaysGold, quotedPrice.Value,
                        request.CouponFlag, request.CouponSlot));
                if (result.Status != StorageMutationStatus.Success)
                {
                    SendStorageError(result.Status);
                    return;
                }
                SendStoragePurchaseAck(
                    request.Currency, request.CouponFlag, request.CouponSlot, result);
                for (int i = 0; i < BoxItems.Count && i < 0x78; i++)
                    if (BoxItems[i] != 0 && _server.IsBoxDisplayable(BoxItems[i]))
                        SendBoxAdd(BoxItems[i], (byte)i, (byte)(1 + BoxLevel[i]), BoxCount[i]);
                SendStorageBalances();
            }
            finally
            {
                ShopBuyInProgress = false;
                FinishInventoryMutation();
            }
        }

        private async Task HandleStorageSaleAsync(byte[] data)
        {
            if (!InventorySellRequest.TryParse(data, out var request) || request.Slot >= 0x78)
            {
                Disconnect(0x3b);
                return;
            }
            byte uiStatus = _inventoryUiState.MutationStatus(InventoryMutationInProgress);
            if (uiStatus != 0) { SendStorageSaleError(uiStatus); return; }
            if (!TryStartInventoryMutation()) { SendStorageSaleError(2); return; }
            byte slot = request.Slot;
            try
            {
                if (slot >= BoxItems.Count || BoxItems[slot] == 0)
                {
                    SendStorageSaleError(3);
                    return;
                }
                int itemId = BoxItems[slot];
                StorageSaleResult result = await _server.SellStorageAsync(this, slot);
                if (result.Status != StorageMutationStatus.Success)
                {
                    SendStorageSaleError(3);
                    return;
                }
                SendStorageSaleAck(slot, itemId, result);
            }
            finally
            {
                FinishInventoryMutation();
            }
        }

        private void SendStoragePurchaseAck(
            byte currency, byte paymentType, ushort couponSlot,
            StoragePurchaseResult result)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x14).WriteWord(0);
            writer.WriteUInt32((uint)System.Math.Max(0, GameInfoId));
            writer.WriteUInt32((uint)System.Math.Max(0, ActiveCharId));
            writer.WriteByte(currency).WriteInt32((sbyte)paymentType).WriteByte(paymentType);
            if (paymentType == 1)
                writer.WriteInt32(result.CouponRowId).WriteInt32(result.CouponItemId)
                    .WriteInt32(result.CouponDiscount).WriteWord(couponSlot);
            writer.WriteByte(0).WriteByte((byte)result.Grants!.Length);
            foreach (StorageGrant grant in result.Grants) writer.WriteUInt32((uint)grant.ItemId);
            foreach (StorageGrant grant in result.Grants) writer.WriteByte((byte)(grant.Cell ?? 0));
            writer.WriteByte(0);
            SendLobby(writer.ToArray());
        }

        private void SendStorageBalances()
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x2e).WriteByte(0);
            writer.WriteUInt32(Gold).WriteUInt32(Cash).WriteByte(0).WriteByte(0);
            SendLobby(writer.ToArray());
        }

        private void SendStorageError(byte status)
            => SendLobby(LobbyFrames.StorageMutationError(false, status));

        private void SendStorageSaleError(byte status)
            => SendLobby(LobbyFrames.StorageMutationError(true, status));

        private void SendStorageSaleAck(byte slot, int itemId, StorageSaleResult result)
        {
            uint itemHandle = result.ItemHandle > 0 ? (uint)result.ItemHandle : 0;
            uint experience = result.Experience switch
            {
                <= 0 => 0,
                >= uint.MaxValue => uint.MaxValue,
                _ => (uint)result.Experience
            };
            SendLobby(LobbyFrames.StorageSaleAck(
                (uint)System.Math.Max(0, GameInfoId),
                (uint)System.Math.Max(0, ActiveCharId), checked((ushort)itemId),
                checked((uint)result.Credit), slot, itemHandle, result.Level, experience));
        }

        private void SendStorageError(StorageMutationStatus status)
        {
            byte wireStatus = status switch
            {
                StorageMutationStatus.NoSpace => 4,
                StorageMutationStatus.InvalidCouponItem or
                StorageMutationStatus.CouponNotForCurrency or
                StorageMutationStatus.CouponDefinitionMissing => (byte)status,
                _ => 3
            };
            SendStorageError(wireStatus);
        }

    }
}
