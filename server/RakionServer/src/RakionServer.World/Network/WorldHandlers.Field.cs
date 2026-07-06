using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Handlers de field/sala/chat. Reconstruidos de worldserv.exe — sao wrappers
    /// finos que validam o estado e delegam o broadcast ao objeto Field/Room
    /// (FUN_004061f0 = broadcast no field; FUN_00404ef0 = broadcast no room).
    /// O conteudo (slots, modo, mapa) e RE incremental dos metodos do Field.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>FUN_0041bca0: chat do canal/game-list + comando "/roominfo &lt;id&gt;". Requer status 2. O
        /// original faz broadcast à lobby-room do usuário (FUN_00404ef0: membros com status==2); aqui o equivalente
        /// é o channel lobby (BroadcastChannelChat) — INCLUINDO o remetente, pois o cliente não desenha o próprio
        /// 0x22 local. O texto já chega como "&lt;nome&gt; : &lt;msg&gt;" (o cliente embute o nome).</summary>
        private static void Op_RoomChat(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status != 0x02) return; // só age no channel lobby / game-list
            string text = ctx.P.CString(0x1000);

            int colon = text.IndexOf(':');
            if (colon >= 0 && text.Length >= colon + 2 + 9 &&
                string.CompareOrdinal(text, colon + 2, "/roominfo", 0, 9) == 0)
            {
                // "/roominfo <id>" — envia info do field ao solicitante (serializacao stub)
                int id = ParseIntAt(text, colon + 12);
                Log.Debug("room", "[{0}] /roominfo {1}", u.Slot, id);
                return;
            }

            ctx.World.BroadcastChannelChat(u, text);
        }

        /// <summary>FUN_004244f0: mensagem "field list" — broadcast no field (requer in-field, status 3).</summary>
        private static void Op_FieldList(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x7e); return; }
            if (u.Status != 0x03) { u.Disconnect(0x7f); return; }
            string text = ctx.P.CString(0x81);
            if (text.Length >= 0x81) { u.Disconnect(0x80); return; }
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            using var w = new PacketWriter();
            w.WriteWord(0x47).WriteByte(0).WriteCString(text);
            field.Broadcast(w.ToArray());
        }

        /// <summary>FUN_00424640: status no field (requer in-field, status 3).</summary>
        private static void Op_FieldStatus(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x81); return; }
            if (u.Status != 0x03) { u.Disconnect(0x82); return; }
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            // FUN_00408440(field, status) — atualiza/broadcast o status do jogador no field
            using var w = new PacketWriter();
            w.WriteWord(0x48).WriteByte((byte)u.Slot);
            field.Broadcast(w.ToArray());
            Log.Debug("field", "[{0}] field status", u.Slot);
        }

        private static int ParseIntAt(string s, int start)
        {
            if (start >= s.Length) return -1;
            int end = start;
            while (end < s.Length && (char.IsDigit(s[end]) || (end == start && s[end] == '-'))) end++;
            return int.TryParse(s.AsSpan(start, end - start), out int v) ? v : -1;
        }
    }
}
