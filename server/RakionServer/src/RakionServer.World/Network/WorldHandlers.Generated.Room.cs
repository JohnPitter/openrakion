using System.Linq;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_InventoryEnter(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.GameInfoId <= 0 || u.ActiveCharId <= 0) { u.Disconnect(0x32); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x33); return; }
            u.HandleInventoryEnter();
        }

        private static void Op_InventoryLeave(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.GameInfoId <= 0 || u.ActiveCharId <= 0) { u.Disconnect(0x34); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x35); return; }
            u.HandleInventoryLeave();
        }

        private static void Op_FieldInvitation(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xd6); return; }
            if (u.Status is not (UserStatus.FieldLobby or UserStatus.InField))
            {
                u.Disconnect(0xd7);
                return;
            }

            ushort targetSlot = ctx.P.UInt16();
            var target = ctx.World.Sessions.FirstOrDefault(s => s.Slot == targetSlot);
            if (target == null || !target.InField) { u.Disconnect(0xd8); return; }

            var field = ctx.World.GetField(u.FieldId);
            if (field == null) { u.Disconnect(0xd6); return; }

            target.SendLobby(FieldInvitationFrames.Notification(
                u.Slot, u.CharName ?? string.Empty, (ushort)field.Id, field));
            Log.Info("field", "[{0}] convidou sessão {1} para field {2}", u.Slot, targetSlot, field.Id);
        }

        private static void Op_EnchantPreview(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xe2); return; }
            if (u.Status != 0x02) { u.Disconnect(0xe3); return; }

            byte target = ctx.P.Byte();
            byte catalyst = ctx.P.Byte();
            byte count = ctx.P.Byte();
            if (count >= 4) { u.Disconnect(0xe4); return; }
            var materials = new byte[count];
            for (int i = 0; i < count; i++) materials[i] = ctx.P.Byte();
            _ = u.HandleEnchantPreviewAsync(target, catalyst, materials);
        }

        private static void Op_BuyLotto(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xe5); return; }
            if (u.Status != 0x02) { u.Disconnect(0xe6); return; }
            _ = u.HandleLotteryPurchaseAsync(ctx.Raw);
        }

        private static void Op_AskLotto(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xe8); return; }
            if (u.Status != 0x02) { u.Disconnect(0xe9); return; }
            _ = u.HandleLotteryPageAsync(ctx.Raw);
        }
    }
}
