using System;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Listagem/gerencia de fields e rooms (opcodes 0x35/0x38/0x39). Validacao de
    /// estado fiel ao exe; FieldListReq lista os fields reais do World; slot/title
    /// confirmam (helpers FUN_0040b080 etc. = RE incremental).
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>FUN_00422850: muda slot na sala. in-field + status 2. Eco 0x18 ou erro 0x35.</summary>
        private static void Op_RoomSlot(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x41); return; }
            if (u.Status != 0x02) { u.Disconnect(0x42); return; }
            byte type = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            ushort val = type != 0 && ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
            using var w = new PacketWriter();
            w.WriteByte(type);
            if (type == 1) w.WriteWord(val);
            u.SendMessage(0x18, w.ToArray());
        }

        /// <summary>FUN_00423100: define titulo/nome da sala. in-field + status 2. Eco 0x26.</summary>
        private static void Op_RoomSetTitle(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x4a); return; }
            if (u.Status != 0x02)
            {
                using var e = new PacketWriter();
                e.WriteWord(0x38).WriteByte(1);
                u.SendLobby(e.ToArray());
                return;
            }
            ctx.P.UInt16(); // count/id
            string title = ctx.P.CString();
            using var w = new PacketWriter();
            w.WriteCString(title);
            u.SendMessage(0x26, w.ToArray());
            Log.Debug("room", "[{0}] set title '{1}'", u.Slot, title);
        }

        /// <summary>FUN_00423300: lista os fields disponiveis. in-field + status 2. Reply subtype 0x39.</summary>
        private static void Op_FieldListReq(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x4e); return; }
            if (u.Status != 0x02) { u.Disconnect(0x4f); return; }
            using var w = new PacketWriter();
            w.WriteWord(0x39);
            lock (ctx.World.Fields)
            {
                w.WriteWord(ctx.World.Fields.Count);
                foreach (var f in ctx.World.Fields)
                {
                    w.WriteWord(f.Id);
                    w.WriteFixed(f.Name, 16);
                    w.WriteByte(f.Mode);
                    w.WriteByte((byte)f.Count);
                    w.WriteByte(f.MaxPlayers);
                    w.WriteByte((byte)(f.InGame ? 1 : 0));
                }
            }
            u.SendLobby(w.ToArray());
            Log.Debug("field", "[{0}] field list ({1} fields)", u.Slot, ctx.World.Fields.Count);
        }
    }
}
