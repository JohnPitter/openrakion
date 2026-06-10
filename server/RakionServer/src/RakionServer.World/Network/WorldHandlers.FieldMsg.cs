using System;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Mensagens curtas dentro do field (eco de volta ao remetente via FUN_0041b940).
    /// Todas exigem o usuario em field (user+0x1460 != 0). Transcritas de worldserv.exe.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>FUN_0041fcd0: acao no field — texto(&lt;=12) + 2 bytes (b1&lt;5, b2&lt;6). Eco subtype 7.</summary>
        private static void Op_FieldAction(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && !u.FieldSecondary)) { u.Disconnect(0x19); return; }
            string text = ctx.P.CString();
            if (text.Length >= 0x0d) { u.Disconnect(0x1a); return; }
            byte b1 = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            if (b1 >= 5) { u.Disconnect(0x1b); return; }
            byte b2 = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            if (b2 >= 6) { u.Disconnect(0xea); return; }
            using var w = new PacketWriter();
            w.WriteCString(text).WriteBytes(new byte[4]).WriteByte(b1).WriteByte(b2);
            u.SendMessage(7, w.ToArray());
        }

        /// <summary>FUN_0041fe10: [u32 valor][nome&lt;=10] no field. Eco subtype 8.</summary>
        private static void Op_FieldName(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && !u.FieldSecondary)) { u.Disconnect(0x1c); return; }
            int val = ctx.P.CanRead(4) ? ctx.P.Int32() : 0;
            string name = ctx.P.CString(0x0b);
            using var w = new PacketWriter();
            w.WriteInt32(val).WriteCString(name);
            u.SendMessage(8, w.ToArray());
        }

        /// <summary>FUN_00420120: texto(&lt;=12) no field. Eco subtype 0x0b.</summary>
        private static void Op_FieldText(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField) { u.Disconnect(0x1f); return; }
            string text = ctx.P.CString();
            if (text.Length >= 0x0d) { u.Disconnect(0x20); return; }
            using var w = new PacketWriter();
            w.WriteCString(text);
            u.SendMessage(0x0b, w.ToArray());
        }
    }
}
