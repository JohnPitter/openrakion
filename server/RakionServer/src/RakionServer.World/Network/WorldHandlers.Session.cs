using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_SuccessUdp(HandlerContext context) =>
            context.User.BeginSuccessUdp(context.Raw);

        private static void Op_InventoryStackPotion(HandlerContext context) =>
            context.User.BeginInventoryStackPotion(context.Raw);

        private static void Op_InventoryMove(HandlerContext context) =>
            context.User.BeginInventoryMove(context.Raw);

        private static void Op_InventoryBuy(HandlerContext context) =>
            context.User.BeginInventoryBuy(context.Raw);

        private static void Op_InventorySell(HandlerContext context) =>
            context.User.BeginInventorySell(context.Raw);

        private static void Op_InventoryBuyBag(HandlerContext context) =>
            context.User.BeginInventoryBuyBag(context.Raw);

        private static void Op_InventoryBuyCharacterSlot(HandlerContext context) =>
            context.User.BeginInventoryBuyCharacterSlot(context.Raw);

        private static void Op_InventoryBuyPowerUser(HandlerContext context) =>
            context.User.BeginInventoryBuyPowerUser(context.Raw);

        private static void Op_PresentPeek(HandlerContext context) =>
            context.User.BeginPresentPeek();

        private static void Op_PresentAccept(HandlerContext context) =>
            context.User.BeginPresentAccept(context.Raw);

        private static void Op_PresentDispose(HandlerContext context) =>
            context.User.BeginPresentDispose(context.Raw);

        private static void Op_InventoryBuyPotionSlot(HandlerContext context) =>
            context.User.BeginInventoryBuyPotionSlot();

        private static void Op_InventoryBuyStageRankClear(HandlerContext context) =>
            context.User.BeginInventoryBuyStageRankClear();

        private static void Op_InventoryBuyStageLevelFree(HandlerContext context) =>
            context.User.BeginInventoryBuyStageLevelFree();

        private static void Op_FieldGameStagePoint(HandlerContext context) =>
            context.User.BeginFieldGameStagePoint(context.Raw);

        private static void Op_FieldList(HandlerContext context) =>
            context.User.BeginFieldList(context.Raw);

        private static void Op_FieldEnter(HandlerContext context) =>
            context.User.BeginFieldEnter(context.Raw);

        private static void Op_FieldQuickEnter(HandlerContext context) =>
            context.User.BeginFieldQuickEnter();

        private static void Op_FieldCreate(HandlerContext context) =>
            context.User.BeginFieldCreate(context.Raw);

        private static void Op_FieldExit(HandlerContext context) =>
            context.User.BeginFieldExit();

        private static void Op_FieldGameRoundStart(HandlerContext context) =>
            context.User.BeginFieldGameRoundStart();

        private static void Op_DisconnectNotText(HandlerContext context)
        {
            Log.Info("[RW] ### CWorld::NetworkMessageDisconnect # Disconnect 1 Not Text");
            context.User.Disconnect(0x01);
        }
    }
}
