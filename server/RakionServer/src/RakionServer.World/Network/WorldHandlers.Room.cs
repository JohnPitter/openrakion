using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Handlers de room/field que validam o estado e delegam a metodos do objeto
    /// Room/Field (FUN_00404da0/00405240/00406240 etc.). A logica profunda desses
    /// metodos (slots, estado da partida) e RE incremental; aqui fica a validacao
    /// fiel (codigos DISC) + a delegacao estrutural.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>FUN_00429230: acao de room (sai/atualiza). Requer in-field. DISC 0xdd.</summary>
        private static void Op_RoomAction(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xdd); return; }
            var room = ctx.World.GetRoom(u.RoomId);
            room?.Remove(u); // FUN_00404da0(room, slot)
            Log.Debug("room", "[{0}] room action (room {1})", u.Slot, u.RoomId);
        }

        /// <summary>FUN_0041bc10: limpeza de sessao ao sair de room/field. Sem DISC.</summary>
        private static void Op_SessionCleanup(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.FieldSecondary) return;
            if (u.Status == 0x02)
            {
                ctx.World.GetRoom(u.RoomId)?.Remove(u); // FUN_00405240(room, slot)
                u.Status = UserStatus.Connected;        // volta a 1
                u.RoomId = -1;
            }
            ctx.World.GetField(u.FieldId)?.Remove(u);    // FUN_0040bf30/af40 (cleanup do field)
            u.InField = false;
            u.FieldSecondary = false;
            u.FieldId = -1;
            Log.Debug("room", "[{0}] session cleanup", u.Slot);
        }

        /// <summary>FUN_00420c20: acao dentro de um field por id. Requer in-field + status 3.</summary>
        private static void Op_FieldJoinAction(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x2e); return; }
            if (u.Status != 0x03) return;
            ushort fieldId = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
            var field = ctx.World.GetField(fieldId);
            if (field == null) { u.Disconnect(0x2f); return; }
            // FUN_00406240(field, slot, u32) — acao no field (entrar/observar)
            Log.Debug("field", "[{0}] field action no field {1}", u.Slot, fieldId);
        }

        /// <summary>FUN_00420de0: ready/confirm em room. Requer in-field + status 2 (room).</summary>
        private static void Op_RoomReady(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x32); return; }
            if (u.Status != 0x02) { u.Disconnect(0x33); return; }
            // FUN_0040b000(user): se "ocupado/ja pronto" -> notice 0x2c; senao confirma (subtype 0x12).
            using var w = new PacketWriter();
            w.WriteWord(0x2c).WriteInt32(0);
            u.SendLobby(w.ToArray());
            Log.Debug("room", "[{0}] room ready", u.Slot);
        }

        /// <summary>FUN_00420cb0: notifica um slot especifico no field (subtype 0x2a).</summary>
        private static void Op_FieldNotifySlot(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x30); return; }
            if (u.Status != 0x03) return;
            ushort slot = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
            int val = ctx.P.CanRead(4) ? ctx.P.Int32() : 0;
            if (slot >= ctx.World.MaxUser) { u.Disconnect(0x31); return; }
            foreach (var s in ctx.World.Sessions)
            {
                if (s.Slot == slot && s.Status == 0x02)
                {
                    using var w = new PacketWriter();
                    w.WriteWord(0x2a).WriteWord((int)u.Slot).WriteInt32(val);
                    s.SendLobby(w.ToArray());
                    break;
                }
            }
        }
    }
}
