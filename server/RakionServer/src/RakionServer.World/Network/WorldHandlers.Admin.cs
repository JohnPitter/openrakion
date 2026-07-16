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

        /// <summary>FUN_0041f1a0: AdminBan apenas ecoa flag + texto curto; não persiste ban.</summary>
        private static void Op_AdminBanEcho(HandlerContext ctx)
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

        /// <summary>FUN_0041f290: notice por escopo 0/2/3 e nome opcional.</summary>
        private static void Op_AdminNotice(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!CanChat(u)) { u.Disconnect(0x0d); return; }
            byte scope = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            string name = ctx.P.CString();
            if (name.Length >= 0x0c) { u.Disconnect(0x0e); return; }
            string msg = ctx.P.CString();
            if (msg.Length >= 0x81) { u.Disconnect(0x0f); return; }

            // payload p/ o destinatario (subtype 99 = 0x63): [u16 0x63][msg\0]
            byte[] whisper;
            using (var w = new PacketWriter()) { w.WriteWord(99).WriteCString(msg); whisper = w.ToArray(); }

            var audience = new AdminNoticeAudience(scope, name);
            bool delivered = false;
            foreach (var s in ctx.World.Sessions)
            {
                var recipient = new AdminNoticeRecipient(
                    s.Status, s.CharName, s.InField, s.FieldSecondary);
                if (!s.Authenticated || !audience.Includes(recipient)) continue;

                s.SendLobby(whisper);
                delivered = true;
                if (name.Length > 0) break;
            }
            if (name.Length > 0)
            {
                using var ack = new PacketWriter();
                ack.WriteWord(5).WriteByte(delivered ? 0 : 1); // 0=entregue, 1=offline
                u.SendLobby(ack.ToArray());
            }
            Log.Info("gm", "[{0}] notice escopo={1} alvo='{2}' ({3})",
                u.Slot, scope, name, delivered ? "entregue" : "sem destinatário");
        }

        /// <summary>FUN_00429030: GM grava variaveis de servidor (this+0x51c8[idx]=val).</summary>
        private static void Op_GmSetVars(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.CanExecuteGm(GmPermission.VariablesWrite)) { u.Disconnect(199); return; }
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
            if (u.Status != UserStatus.LobbyGm ||
                !u.CanExecuteGm(GmPermission.QueryEntry))
            {
                u.Disconnect(0x11);
                return;
            }
            if (!GmFieldEntryRequest.TryParse(ctx.Raw, out var request))
            {
                u.Disconnect(0x11);
                return;
            }

            GmFieldEntrySnapshot snapshot = ctx.World.QueryGmFieldEntry(request.FieldId);
            u.SendLobby(GmFieldEntryFrames.Response(snapshot));
            Log.Debug("gm", "[{0}] query field {1}: status={2}",
                u.Slot, request.FieldId, snapshot.Status);
        }

        /// <summary>FUN_00429140: GM le variaveis de servidor.</summary>
        private static void Op_GmGetVars(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.CanExecuteGm(GmPermission.VariablesRead)) { u.Disconnect(200); return; }
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

        /// <summary>FUN_0041f480: GM define os MD5 globais do cliente em runtime.</summary>
        private static void Op_GmSetClientMd5(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.CanExecuteGm(GmPermission.ClientHashWrite)) { u.Disconnect(0x10); return; }
            string md5_1 = ctx.P.CString();
            string md5_2 = ctx.P.CString();
            if (!ClientHashPolicy.IsMd5(md5_1) || !ClientHashPolicy.IsMd5(md5_2))
            {
                u.Disconnect(0x10);
                return;
            }
            ctx.World.Config.UpdateClientHashes(md5_1, md5_2);
            Log.Info("gm", "[{0}] atualizou os hashes MD5 do cliente", u.Slot);
            using var w = new PacketWriter();
            w.WriteWord(0x0b).WriteByte(0).WriteCString(md5_1).WriteCString(md5_2);
            u.SendLobby(w.ToArray());
        }

        private static void Op_GmOperationIpGate(HandlerContext ctx)
        {
            byte? reason = GmOperationPolicy.DisconnectReason(
                ctx.User.SubStatus, ctx.User.RemoteIp,
                ctx.World.Config.GmOperationAllowedIps);
            if (reason.HasValue)
            {
                Log.Warn("gm", "[{0}] operação GM recusada: substatus={1:X2}, ip={2}, disc={3:X2}",
                    ctx.User.Slot, ctx.User.SubStatus, ctx.User.RemoteIp, reason.Value);
                ctx.User.Disconnect(reason.Value);
            }
        }

        private static void Op_KeepAlive(HandlerContext context)
        {
            long elapsed = context.User.RecordKeepAlive();
            if (KeepAliveRules.IsLate(elapsed))
                Log.Warn("net", "[{0}] keepalive atrasado: {1}s",
                    context.User.Slot, elapsed / 1000);
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
