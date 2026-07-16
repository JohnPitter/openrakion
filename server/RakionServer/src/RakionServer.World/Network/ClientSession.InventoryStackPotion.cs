using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void BeginInventoryStackPotion(byte[] data)
        {
            if (!InventoryStackPotionRequest.TryParse(data, out var request))
            {
                Disconnect(0xE0);
                return;
            }
            if (request.Source >= 0x78) { Disconnect(0xE0); return; }
            if (request.Destination >= 0x78) { Disconnect(0xE1); return; }

            byte status = _inventoryUiState.MutationStatus(ShopBuyInProgress);
            bool acquired = status == 0 && TryStartInventoryMutation();
            if (status == 0 && !acquired) status = 2;
            if (acquired)
            {
                try
                {
                    status = InventoryStackPotionRules.Validate(
                        _server.FindItemDef(BoxItems[request.Source]),
                        _server.FindItemDef(BoxItems[request.Destination]));
                }
                finally
                {
                    FinishInventoryMutation();
                }
            }
            if (status != 0)
            {
                SendEncryptedFrame(InventoryStackPotionFrames.Error(status));
                Log.Warn("inventory", "[{0}] stack {1}->{2} rejeitado: {3}",
                    Slot, request.Source, request.Destination, status);
                return;
            }

            SendMessage(0x27, InventoryStackPotionFrames.SuccessBody(
                GameInfoId, ActiveCharId, request.Source, request.Destination, BoxItems));
            Log.Info("inventory", "[{0}] stack {1}->{2} confirmado", Slot,
                request.Source, request.Destination);
        }
    }
}
