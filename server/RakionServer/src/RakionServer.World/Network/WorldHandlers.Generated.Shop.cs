using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_InventoryAllocationPoint(HandlerContext ctx)
        {
            ClientSession user = ctx.User;
            if (!user.InField || !user.FieldSecondary) { user.Disconnect(0x43); return; }
            if (user.Status != 0x02) { user.Disconnect(0x44); return; }
            byte stat = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            if (stat > 9) { SendAllocationResult(user, 5); return; }
            _ = user.HandleStatAllocationAsync(stat);
        }

        private static void SendAllocationResult(ClientSession user, byte status)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x33).WriteByte(status);
            user.SendLobby(writer.ToArray());
        }
    }
}
