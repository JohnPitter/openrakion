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

        /// <summary>FUN_00423100 (op0x38): JOIN de sala pela game list. parse [u16 fieldIndex][senha\0 &lt;9].
        /// Gate InField&amp;&amp;FieldSecondary (disc 0x4a); Status==2 senão reply [0x38][05]. Acha a sala pelo
        /// fieldIndex, adiciona o joiner ao Field (member-join, MESMO trilho do bot) e transiciona ao room lobby:
        /// manda ao joiner o roster (0x38 de cada ocupante + o próprio) e faz broadcast do joiner aos demais.
        /// Regra no domínio (<see cref="WorldServer.TryJoinRoom"/>); aqui só bytes↔chamada + serialização.</summary>
        private static void Op_RoomJoin(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x4a); return; }
            if (u.Status != 0x02)
            {
                using var e = new PacketWriter();
                e.WriteWord(0x38).WriteByte(5);
                u.SendLobby(e.ToArray());
                return;
            }
            ushort fieldIndex = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0xffff;
            string pass = ctx.P.CanRead(1) ? ctx.P.CString(9) : "";

            var f = ctx.World.TryJoinRoom(u, fieldIndex, pass, out int seat, out byte err);
            if (f == null || seat < 0)
            {
                using var e = new PacketWriter();
                e.WriteWord(0x38).WriteByte(err == 0 ? (byte)0x4c : err);
                u.SendLobby(e.ToArray());
                Log.Info("room", "[{0}] join sala {1} FALHOU (err 0x{2:x2})", u.Slot, fieldIndex, err);
                return;
            }

            // worldserv FUN_00406f40: (1) broadcast 0x38 member-join do joiner a TODOS os membros do field
            // (inclui o joiner); (2) 0x37 estado COMPLETO da sala SÓ ao joiner (transiciona game-list -> tela da sala).
            var join = WorldServer.BuildMemberJoin(u.CharName, u.CharClass, u.CharLevel, (ushort)u.GameInfoId, seat, peerEp: u.UdpEndpoint);
            lock (f.Players) foreach (var m in f.Players) m.SendLobby(join);
            u.SendLobby(WorldServer.BuildRoomState(f));
            Log.Ok("room", "[{0}] '{1}' ENTROU na sala {2} seat {3} (host [{4}]) — 0x37 room-state + 0x38 broadcast",
                u.Slot, u.CharName, fieldIndex, seat, f.Master?.Slot ?? -1);
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
