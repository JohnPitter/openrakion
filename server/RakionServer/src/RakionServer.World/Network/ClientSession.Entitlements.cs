using System;
using System.Threading.Tasks;
using RakionServer.World.Database;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void BeginInventoryBuyBag(byte[] data) =>
            _ = HandleEntitlementPurchaseAsync(InventoryEntitlement.Bag, data);

        internal void BeginInventoryBuyCharacterSlot(byte[] data) =>
            _ = HandleEntitlementPurchaseAsync(InventoryEntitlement.CharacterSlot, data);

        internal void BeginInventoryBuyPotionSlot() => _ = HandlePotionSlotPurchaseAsync();

        internal void BeginInventoryBuyStageRankClear() => _ = HandleStageRankClearAsync();

        internal void BeginInventoryBuyStageLevelFree() => _ = HandleStageLevelFreeAsync();

        private async Task HandleEntitlementPurchaseAsync(
            InventoryEntitlement entitlement, byte[] data)
        {
            if (!InventoryEntitlementPurchaseRequest.TryParse(data, out var request))
            {
                SendEntitlementResult(entitlement,
                    new EntitlementPurchaseResult(EntitlementPurchaseStatus.Failed));
                return;
            }
            byte uiStatus = _inventoryUiState.MutationStatus(InventoryMutationInProgress);
            if (uiStatus != 0)
            {
                SendEntitlementResult(entitlement,
                    new EntitlementPurchaseResult((EntitlementPurchaseStatus)uiStatus));
                return;
            }
            if (!TryStartInventoryMutation())
            {
                SendEntitlementResult(entitlement,
                    new EntitlementPurchaseResult(EntitlementPurchaseStatus.InProgress));
                return;
            }

            ShopBuyInProgress = true;
            try
            {
                EntitlementPurchaseResult result = await _server.PurchaseEntitlementAsync(
                    this, entitlement, request.UsesCoupon ? (byte)1 : (byte)0,
                    request.UsesCoupon ? request.CouponSlot : (ushort)0);
                SendEntitlementResult(entitlement, result);
            }
            finally
            {
                ShopBuyInProgress = false;
                FinishInventoryMutation();
            }
        }

        private void SendEntitlementResult(
            InventoryEntitlement entitlement, EntitlementPurchaseResult result) =>
            SendEncryptedFrame(LobbyFrames.InventoryEntitlementAck(entitlement, result));

        private async Task HandlePotionSlotPurchaseAsync()
        {
            if (PotionSlotCount is < 3 or > 5)
            {
                Disconnect(0xd5);
                return;
            }
            PotionSlotPurchaseResult result = await _server.PurchasePotionSlotAsync(this);
            SendPotionSlotResult(result);
        }

        private void SendPotionSlotResult(PotionSlotPurchaseResult result) =>
            SendEncryptedFrame(LobbyFrames.PotionSlotPurchaseAck(result));

        private async Task HandleStageRankClearAsync()
        {
            StageRankClearResult result = await _server.PurchaseStageRankClearAsync(this);
            SendStageRankClearResult(result);
        }

        private void SendStageRankClearResult(StageRankClearResult result) =>
            SendEncryptedFrame(LobbyFrames.StageRankClearAck(result));

        private async Task HandleStageLevelFreeAsync()
        {
            StageLevelFreeResult result = await _server.PurchaseStageLevelFreeAsync(this);
            SendStageLevelFreeResult(result);
        }

        private void SendStageLevelFreeResult(StageLevelFreeResult result) =>
            SendEncryptedFrame(LobbyFrames.StageLevelFreeAck(result));
    }
}
