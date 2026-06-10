using System;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Configuracao de sala (modo/mapa/item/opcao) — opcodes 0x31-0x33. A validacao
    /// de estado (status 2 = em room) e os limites sao fieis ao exe; a aplicacao da
    /// regra (helpers FUN_0040cf10/0040b080/0040b3d0) e RE incremental, entao por ora
    /// confirmamos sucesso (result=0) e ecoamos os parametros.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>FUN_00421870: define modo/mapa (2 pares; limites 0x78/0x13). Eco subtype 0x31.</summary>
        private static void Op_RoomSetMode(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x3c); return; }
            if (u.Status != 0x02) { u.Disconnect(0x3d); return; }
            byte a = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            byte b = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            byte c = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            byte d = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            bool okB = a == 0 ? b < 0x78 : b < 0x13;
            bool okD = c == 0 ? d < 0x78 : d < 0x13;
            if (!(okB && okD)) { u.Disconnect(0x3e); return; }
            using var w = new PacketWriter();
            w.WriteWord(0x31).WriteByte(0).WriteByte(a).WriteByte(b).WriteByte(c).WriteByte(d);
            u.SendLobby(w.ToArray());
        }

        /// <summary>FUN_004226b0: define item da sala (helper FUN_0040b080). Eco subtype 0x16/0x18 ou erro 0x32.</summary>
        private static void Op_RoomSetItem(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x3f); return; }
            if (u.Status != 0x02) { u.Disconnect(0x40); return; }
            byte type = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            ushort val = type != 0 && ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
            // helper FUN_0040b080 -> 0 = ok; aqui assumimos ok e confirmamos (subtype 0x16/0x18)
            using var w = new PacketWriter();
            w.WriteByte(type);
            if (type == 1) w.WriteWord(val);
            u.SendMessage(type == 1 ? (ushort)0x18 : (ushort)0x16, w.ToArray());
        }

        /// <summary>FUN_004229f0: opcao de sala (helper FUN_0040b3d0). Eco subtype 0x33.</summary>
        private static void Op_RoomSetOption(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x43); return; }
            if (u.Status != 0x02) { u.Disconnect(0x44); return; }
            byte opt = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            using var w = new PacketWriter();
            w.WriteWord(0x33).WriteByte(0).WriteByte(opt); // result=0
            u.SendLobby(w.ToArray());
        }
    }
}
