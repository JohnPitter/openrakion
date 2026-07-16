using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Handlers de room/field que validam o estado e delegam a metodos do objeto
    /// Canal/Field (FUN_00404da0/00405240/00406240 etc.).
    /// metodos (slots, estado da partida) e RE incremental; aqui fica a validacao
    /// fiel (codigos DISC) + a delegacao estrutural.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>FUN_00429230: devolve uma amostra de até oito membros do canal.</summary>
        private static void Op_ChannelCharacters(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xdd); return; }
            ctx.World.SendChannelSnapshot(u, 8);
            Log.Debug("channel", "[{0}] amostra de membros do canal {1}", u.Slot, u.ChannelId);
        }

        /// <summary>FUN_0041bc10: limpeza de sessao ao sair de room/field. Sem DISC.</summary>
        private static void Op_SessionCleanup(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.FieldSecondary) return;
            if (u.Status == 0x02)
            {
                ctx.World.LeaveChannel(u, true);        // FUN_00405240(channel, localSlot)
                u.Status = UserStatus.Connected;        // volta a 1
            }
            ctx.World.LeaveField(u);                    // FUN_0040bf30/af40 (cleanup do field)
            u.InField = false;
            u.FieldSecondary = false;
            u.FieldId = -1;
            Log.Debug("channel", "[{0}] session cleanup", u.Slot);
        }

        /// <summary>FUN_00420c20/00406240: solicita ping ao master de um field listado.</summary>
        private static void Op_ChannelFieldPingRequest(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x2e); return; }
            if (u.Status != UserStatus.InField) return;
            if (!ctx.P.CanRead(6)) return;
            ushort fieldId = ctx.P.UInt16();
            uint tick = ctx.P.UInt32();
            if (fieldId >= ctx.World.Config.MaxField) { u.Disconnect(0x2f); return; }
            var field = ctx.World.GetField(fieldId);
            if (field == null) return;
            ClientSession? master = field.RecAt(field.MasterSlot)?.Session;
            if (field.State == 0 || master == null) return;
            using var writer = new PacketWriter();
            writer.WriteWord(u.Slot).WriteUInt32(tick);
            master.SendLobby(Prefix(0x29, writer.ToArray()));
        }

        /// <summary>FUN_00420cb0: devolve o ping ao usuário global solicitado.</summary>
        private static void Op_ChannelFieldPingResponse(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x30); return; }
            if (u.Status != UserStatus.InField) return;
            if (!ctx.P.CanRead(6)) return;
            ushort targetSlot = ctx.P.UInt16();
            uint tick = ctx.P.UInt32();
            if (targetSlot >= ctx.World.MaxUser) { u.Disconnect(0x31); return; }
            ClientSession? target = ctx.World.GetSession(targetSlot);
            if (target?.Status != UserStatus.FieldLobby) return;
            using var writer = new PacketWriter();
            writer.WriteWord((ushort)u.FieldId).WriteUInt32(tick);
            target.SendLobby(Prefix(0x2a, writer.ToArray()));
        }
    }
}
