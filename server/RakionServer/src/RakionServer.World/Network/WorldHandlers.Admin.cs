using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Handlers reconstruidos de chat, comandos de GM, keepalive e anticheat
    /// (opcodes 0x04,0x05,0x08-0x0b,0x0e-0x10). Transcritos de worldserv.exe
    /// (handlers.out.txt). Sub-estado GM = UserStatus.LobbyGm (5).
    /// </summary>
    public static partial class WorldHandlers
    {
        // substatus aceitos em chat/sala (user+0x146c): 0x01 e '4' (0x34)
        private const byte SubA = 0x01;
        private const byte SubB = 0x34;

        private static bool CanChat(ClientSession u) =>
            u.Status == UserStatus.LobbyGm || u.SubStatus == SubA || u.SubStatus == SubB;

        /// <summary>FUN_0041f1a0: eco de texto curto (&lt;=12) de volta ao usuario.</summary>
        private static void Op_SetUserText(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!CanChat(u)) { u.Disconnect(0x0b); return; }
            byte flag = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            string s = ctx.P.CString();
            if (s.Length >= 0x0d) { u.Disconnect(0x0c); return; }
            // FUN_0041b940 (plano): [u16 sessionId][u16 1][u8 flag][s\0]
            using var w = new PacketWriter();
            w.WriteByte(flag).WriteCString(s);
            u.SendMessage(1, w.ToArray());
        }

        /// <summary>FUN_0041f290: whisper/mensagem privada por nome.</summary>
        private static void Op_Whisper(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!CanChat(u)) { u.Disconnect(0x0d); return; }
            byte type = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            string name = ctx.P.CString();
            if (name.Length >= 0x0c) { u.Disconnect(0x0e); return; }
            string msg = ctx.P.CString();
            if (msg.Length >= 0x81) { u.Disconnect(0x0f); return; }

            // payload p/ o destinatario (subtype 99 = 0x63): [u16 0x63][msg\0]
            byte[] whisper;
            using (var w = new PacketWriter()) { w.WriteWord(99).WriteCString(msg); whisper = w.ToArray(); }

            bool delivered = false;
            foreach (var s in ctx.World.Sessions)
            {
                if (s.Authenticated && string.Equals(s.CharName, name, StringComparison.OrdinalIgnoreCase))
                {
                    s.SendLobby(whisper);
                    delivered = true;
                    if (type != 0) break;
                }
            }
            if (type != 0)
            {
                using var ack = new PacketWriter();
                ack.WriteWord(5).WriteByte(delivered ? 0 : 1); // 0=entregue, 1=offline
                u.SendLobby(ack.ToArray());
            }
            Log.Debug("chat", "[{0}] whisper -> '{1}' ({2})", u.Slot, name, delivered ? "ok" : "offline");
        }

        /// <summary>FUN_00429030: GM grava variaveis de servidor (this+0x51c8[idx]=val).</summary>
        private static void Op_GmSetVars(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status != UserStatus.LobbyGm) { u.Disconnect(199); return; }
            int count = ctx.P.CanRead(2) ? ctx.P.UInt16() : 0;
            byte fails = 0;
            for (int i = 0; i < count && ctx.P.CanRead(8); i++)
            {
                uint idx = (uint)ctx.P.Int32();
                uint val = (uint)ctx.P.Int32();
                if (idx < 0x200) ctx.World.GmVars[idx] = val; else fails++;
            }
            using var w = new PacketWriter();
            w.WriteWord(8).WriteByte(fails);
            u.SendLobby(w.ToArray());
            Log.Info("gm", "[{0}] set {1} vars ({2} falhas)", u.Slot, count, fails);
        }

        /// <summary>FUN_0041f5c0: GM consulta entrada (this+0xe4, entradas de 0x3c0 bytes).</summary>
        private static void Op_GmQueryEntry(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status != UserStatus.LobbyGm) { u.Disconnect(0x11); return; }
            ushort id = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
            // serializacao da entrada (FUN_004058e0) ainda nao reconstruida — responde vazio.
            using var w = new PacketWriter();
            w.WriteWord(9).WriteByte(0); // status: 0 = ok / vazio
            u.SendLobby(w.ToArray());
            Log.Debug("gm", "[{0}] query entry {1} (serializacao stub)", u.Slot, id);
        }

        /// <summary>FUN_00429140: GM le variaveis de servidor.</summary>
        private static void Op_GmGetVars(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status != UserStatus.LobbyGm) { u.Disconnect(200); return; }
            int count = ctx.P.CanRead(2) ? ctx.P.UInt16() : 0;
            using var w = new PacketWriter();
            w.WriteWord(10);
            for (int i = 0; i < count && ctx.P.CanRead(4); i++)
            {
                uint idx = (uint)ctx.P.Int32();
                w.WriteUInt32(idx < 0x200 ? ctx.World.GmVars[idx] : 0u);
            }
            u.SendLobby(w.ToArray());
        }

        /// <summary>FUN_0041f480: GM define os MD5 do client (anti-cheat) e grava no ini.</summary>
        private static void Op_GmSetClientMd5(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status != UserStatus.LobbyGm) { u.Disconnect(0x10); return; }
            string md5_1 = ctx.P.CString();
            string md5_2 = ctx.P.CString();
            ctx.World.Config.ClientMd5_1 = md5_1;
            ctx.World.Config.ClientMd5_2 = md5_2;
            Log.Info("gm", "[{0}] set client MD5_1={1} MD5_2={2}", u.Slot, md5_1, md5_2);
            using var w = new PacketWriter();
            w.WriteWord(0x0b).WriteByte(0).WriteCString(md5_1).WriteCString(md5_2);
            u.SendLobby(w.ToArray());
        }

        /// <summary>FUN_0041fa40: sai do field/sala (volta ao lobby).</summary>
        private static void Op_LeaveField(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && !u.FieldSecondary)) { u.Disconnect(0x16); return; }
            u.InField = false;
            using var w = new PacketWriter();
            w.WriteWord(0x0e).WriteByte(0).WriteInt32(0).WriteInt32(0);
            u.SendLobby(w.ToArray());
            Log.Info("field", "[{0}] saiu do field", u.Slot);
        }

        /// <summary>FUN_0041fb30: keepalive; loga latencia alta (&gt;90s). Sem resposta.</summary>
        private static void Op_KeepAlive(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField) { u.Disconnect(0x1a); return; }
            // latencia real (FUN_0040bbb0) nao modelada; so reconhece o keepalive.
            Log.Debug("net", "[{0}] keepalive", u.Slot);
        }

        /// <summary>
        /// FUN_0041fc00: report de anticheat (GameGuard). No original, falha -> DISC 0x18.
        /// Nossa stack roda sem GameGuard (ver STATUS), entao aceitamos o report.
        /// </summary>
        private static void Op_GameGuardAuth(HandlerContext ctx)
        {
            Log.Debug("ac", "[{0}] GameGuard report ({1} bytes) — aceito (GG desligado)", ctx.User.Slot, ctx.Raw.Length);
        }
    }
}
