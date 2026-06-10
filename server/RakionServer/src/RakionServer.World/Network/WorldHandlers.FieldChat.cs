using System;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Whisper/localizacao/busca dentro do field (opcodes 0x16-0x1a). Transcritos de
    /// worldserv.exe. Usam enumeracao de sessoes para achar usuarios por nome (FUN_0040af20).
    /// </summary>
    public static partial class WorldHandlers
    {
        private static ClientSession? FindOnline(WorldServer world, string name)
        {
            foreach (var s in world.Sessions)
                if (s.Authenticated && s.InField && s.FieldSecondary &&
                    s.SubStatus != 1 && string.Equals(s.CharName, name, StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }

        /// <summary>FUN_00420200: whisper por nome dentro do field. Entrega ao alvo e ecoa ao remetente.</summary>
        private static void Op_FieldWhisper(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x21); return; }
            string name = ctx.P.CString();
            if (name.Length >= 0x0d) { u.Disconnect(0x22); return; }
            string msg = ctx.P.CString();
            if (msg.Length >= 0x81) { u.Disconnect(0x23); return; }

            var target = FindOnline(ctx.World, name);
            if (target != null)
            {
                using var w = new PacketWriter();
                w.WriteWord(0x16).WriteByte(0).WriteCString(u.CharName).WriteCString(msg);
                byte[] pkt = w.ToArray();
                target.SendLobby(pkt);
                u.SendLobby(pkt);   // eco ao remetente
            }
            else
            {
                using var w = new PacketWriter();
                w.WriteWord(0x16).WriteByte(1); // 1 = offline
                u.SendLobby(w.ToArray());
            }
        }

        /// <summary>FUN_00420410: devolve a localizacao do proprio usuario (room/field).</summary>
        private static void Op_GetLocation(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x24); return; }
            byte type; int id;
            if (u.Status == 0x02) { type = 0; id = u.RoomId; }       // em room
            else if (u.Status == 0x03) { type = 1; id = u.FieldId; } // em field
            else { u.Disconnect(0x25); return; }
            using var w = new PacketWriter();
            w.WriteWord(0x17).WriteByte(0).WriteByte(type).WriteWord(id < 0 ? 0 : id);
            u.SendLobby(w.ToArray());
        }

        /// <summary>FUN_00420520: busca a localizacao de outro usuario por nome.</summary>
        private static void Op_FindUser(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x26); return; }
            string name = ctx.P.CString();
            if (name.Length >= 0x0d) { u.Disconnect(0x27); return; }

            var target = FindOnline(ctx.World, name);
            using var w = new PacketWriter();
            if (target != null)
            {
                byte type; int id;
                if (target.Status == 0x02) { type = 0; id = target.RoomId; }
                else { type = 1; id = target.FieldId; }
                w.WriteWord(0x18).WriteByte(0).WriteCString(name).WriteByte(type).WriteWord(id < 0 ? 0 : id);
            }
            else
            {
                w.WriteWord(0x18).WriteByte(1).WriteCString(name); // 1 = nao encontrado
            }
            u.SendLobby(w.ToArray());
        }

        /// <summary>FUN_00420760: comando de texto curto no field. Eco subtype 0x0d.</summary>
        private static void Op_FieldCmd(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField) { u.Disconnect(0x28); return; }
            string text = ctx.P.CString();
            if (text.Length >= 0x0d) { u.Disconnect(0x29); return; }
            using var w = new PacketWriter();
            w.WriteCString(text);
            u.SendMessage(0x0d, w.ToArray());
        }

        /// <summary>FUN_00420840: ping/ack simples no field. Subtype 0x0e.</summary>
        private static void Op_FieldPing(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField) { u.Disconnect(0x2a); return; }
            u.SendMessage(0x0e, new byte[4]);
        }
    }
}
