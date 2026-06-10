using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Tabela de handlers de opcode do World Server — reconstrucao do switch de
    /// FUN_0042ab40 (worldserv.exe). CADA opcode tem um metodo nomeado; os ainda
    /// nao reconstruidos chamam Stub() (logam e citam o FUN_xxxx de origem). Para
    /// "desenvolver uma funcao nova" basta preencher o metodo correspondente.
    ///
    /// Assinaturas dos handlers: (HandlerContext ctx) onde ctx tem World, User,
    /// Opcode, P (PacketReader do payload) e Raw.
    /// </summary>
    public static partial class WorldHandlers
    {
        public delegate void Handler(HandlerContext ctx);

        private readonly record struct Entry(string Name, uint Addr, Handler Fn);

        private static readonly Dictionary<ushort, Entry> Table = Build();

        public static void Dispatch(HandlerContext ctx)
        {
            if (Table.TryGetValue(ctx.Opcode, out var e))
            {
                Log.Debug("op", "[{0}] 0x{1:x2} {2}", ctx.User.Slot, ctx.Opcode, e.Name);
                try { e.Fn(ctx); }
                catch (Exception ex) { Log.Error("op", "[{0}] 0x{1:x2} {2}: {3}", ctx.User.Slot, ctx.Opcode, e.Name, ex.Message); }
                return;
            }
            // fora da tabela -> default do dispatcher FUN_0042ab40
            Log.Warn("op", "[{0}] opcode 0x{1:x2} desconhecido -> DISC {2}", ctx.User.Slot, ctx.Opcode, Protocol.DiscReason.UnknownOpcode);
            ctx.User.Disconnect(Protocol.DiscReason.UnknownOpcode);
        }

        public static string OpName(ushort op) => Table.TryGetValue(op, out var e) ? e.Name : $"op0x{op:x2}";

        /// <summary>Prefixa o payload com [u16 subtype] (helper dos handlers gerados).</summary>
        internal static byte[] Prefix(ushort subtype, byte[] body)
        {
            byte[] r = new byte[2 + body.Length];
            r[0] = (byte)(subtype & 0xff);
            r[1] = (byte)(subtype >> 8);
            System.Array.Copy(body, 0, r, 2, body.Length);
            return r;
        }

        // ===================== TABELA COMPLETA (87 opcodes) =====================
        private static Dictionary<ushort, Entry> Build() => new()
        {
            // --- lobby / sessao (reconstruidos) ---
            [0x01] = new("EnterChannel",      0x41ee00, Op_EnterChannel),
            [0x02] = new("RequestWorldInfo",  0x41ef00, Op_RequestWorldInfo),
            [0x03] = new("GmServerOpenClose", 0x41f060, Op_GmServerOpenClose),
            [0x04] = new("SetUserText",       0x41f1a0, Op_SetUserText),
            [0x05] = new("Whisper",           0x41f290, Op_Whisper),
            [0x08] = new("GmSetVars",         0x429030, Op_GmSetVars),
            [0x09] = new("GmQueryEntry",      0x41f5c0, Op_GmQueryEntry),
            [0x0a] = new("GmGetVars",         0x429140, Op_GmGetVars),
            [0x0b] = new("GmSetClientMd5",    0x41f480, Op_GmSetClientMd5),
            [0x0e] = new("LeaveField",        0x41fa40, Op_LeaveField),
            [0x0f] = new("KeepAlive",         0x41fb30, Op_KeepAlive),   // log "[%04u] ALTO %u" se lat>90s
            [0x10] = new("GameGuardAuth",     0x41fc00, Op_GameGuardAuth),// log "[%04u] GMGD %u" + DISC 0x18
            [0x12] = new("FieldAction",       0x41fcd0, Op_FieldAction),
            [0x13] = new("FieldName",         0x41fe10, Op_FieldName),
            [0x14] = new("SelectCharacter",   0x41fef0, Stub),  // spawn no field (FUN_0040be30/ac30) — RE deep
            [0x15] = new("FieldText",         0x420120, Op_FieldText),
            [0x16] = new("FieldWhisper",      0x420200, Op_FieldWhisper),
            [0x17] = new("GetLocation",       0x420410, Op_GetLocation),
            [0x18] = new("FindUser",          0x420520, Op_FindUser),
            [0x19] = new("FieldCmd",          0x420760, Op_FieldCmd),
            [0x1a] = new("FieldPing",         0x420840, Op_FieldPing),
            [0x1b] = new("PartyInvite",       0x4208e0, Stub),  // FUN_0040bd80 (party) — RE deep
            [0x1c] = new("PartyOp",           0x420a40, Stub),  // FUN_0040bd80 (party) — RE deep
            [0x1e] = new("RoomAction",        0x429230, Op_RoomAction),
            [0x20] = new("SessionCleanup",    0x41bc10, Op_SessionCleanup),
            [0x22] = new("RoomChat",          0x41bca0, Op_RoomChat),  // chat de room + comando "/roominfo"
            // --- field / sala / chat / jogo ---
            [0x29] = new("FieldJoinAction",   0x420c20, Op_FieldJoinAction),
            [0x2a] = new("FieldNotifySlot",   0x420cb0, Op_FieldNotifySlot),
            [0x2c] = new("RoomReady",         0x420de0, Op_RoomReady),
            [0x2d] = new("Op_0x2D_Recon",     0x420f10, Op_0x2D_Recon),  // RoomMemberList
            [0x2e] = new("Op_0x2E_Recon",     0x421210, Op_0x2E_Recon),  // RoomShopBuy/Equip
            [0x2f] = new("Op_0x2F_Recon",     0x4215a0, Op_0x2F_Recon),  // RoomShopList
            [0x31] = new("RoomSetMode",       0x421870, Op_RoomSetMode),
            [0x32] = new("RoomSetItem",       0x4226b0, Op_RoomSetItem),
            [0x33] = new("RoomSetOption",     0x4229f0, Op_RoomSetOption),
            [0x34] = new("Op_0x34_Recon",     0x422b10, Op_0x34_Recon),  // RoomBuyB
            [0x35] = new("RoomSlot",          0x422850, Op_RoomSlot),
            [0x36] = new("Op_0x36_Recon",     0x422c90, Op_0x36_Recon),  // RoomList/FieldSearch
            [0x38] = new("RoomSetTitle",      0x423100, Op_RoomSetTitle),
            [0x39] = new("FieldListReq",      0x423300, Op_FieldListReq),
            [0x3a] = new("Op_0x3A_Recon",     0x4234e0, Op_0x3A_Recon),  // FieldStart/Leave3D
            [0x3b] = new("Op_0x3B_Recon",     0x423580, Op_0x3B_Recon),  // FieldCreate(sala)
            [0x3d] = new("Op_0x3D_Recon",     0x423ad0, Op_0x3D_Recon),  // troca de arma A<->B (FUN_00407520)
            [0x3e] = new("Op_0x3E_Recon",     0x423b70, Op_0x3E_Recon),  // re-spawn/troca de assento
            [0x3f] = new("Op_0x3F_Recon",     0x423c00, Op_0x3F_Recon),  // start-vote/kick
            [0x40] = new("Op_0x40_Recon",     0x423cc0, Op_0x40_Recon),  // destroy/leave-object
            [0x41] = new("Op_0x41_Recon",     0x423dd0, Op_0x41_Recon),  // config-in-field (host settings)
            [0x42] = new("Op_0x42_Recon",     0x424100, Op_0x42_Recon),  // ready/equip-lock toggle
            [0x43] = new("Op_0x43_Recon",     0x424210, Op_0x43_Recon),  // ATTACK / match-start engage
            [0x45] = new("Op_0x45_Recon",     0x4242c0, Op_0x45_Recon),  // spawn/join-into-field
            [0x46] = new("Op_0x46_Recon",     0x424350, Op_0x46_Recon),  // HIT / aplicar dano
            [0x47] = new("FieldList",         0x4244f0, Op_FieldList),  // NetworkMessageFieldList (3D chat)
            [0x48] = new("FieldStatus",       0x424640, Op_FieldStatus),
            [0x4a] = new("Op_0x4A_Recon",     0x4246e0, Op_0x4A_Recon),  // charge/postura-shift
            [0x4b] = new("Op_0x4B_Recon",     0x4247b0, Op_0x4B_Recon),  // MOVE/action relay (exclui sender)
            [0x4c] = new("Op_0x4C_Recon",     0x424880, Op_0x4C_Recon),  // action direcionada a 1 alvo
            [0x4d] = new("Op_0x4D_Recon",     0x424980, Op_0x4D_Recon),  // rotacao/facing 2 eixos
            [0x4f] = new("Op_0x4F_Recon",     0x424a20, Op_0x4F_Recon),  // DIE / morte do player
            [0x50] = new("Op_0x50_Recon",     0x424b60, Op_0x50_Recon),  // GameResult/scoring
            [0x53] = new("Op_0x53_Recon",     0x425010, Op_0x53_Recon),  // NetworkMessageFieldCreate
            [0x61] = new("Op_0x61_Recon",     0x41c270, Op_0x61_Recon),  // FieldReady ack
            [0x56] = new("Op56",              0x425620, Stub),
            [0x57] = new("Op57",              0x4256d0, Stub),
            [0x59] = new("Op59",              0x4257b0, Stub),
            [0x5a] = new("Op5A",              0x425860, Stub),
            [0x5b] = new("Op5B",              0x425990, Stub),
            [0x5d] = new("Op5D",              0x425a70, Stub),
            [0x5e] = new("Op5E",              0x425bb0, Stub),
            [0x60] = new("Op60",              0x425cc0, Stub),
            [0x62] = new("Op62",              0x41c2b0, Stub),
            [0x64] = new("Op64",              0x4283a0, Stub),
            [0x65] = new("Op65",              0x428430, Stub),
            [0x6b] = new("Op6B",              0x4286a0, Stub),
            [0x6c] = new("Op6C",              0x428750, Stub),
            [0x6d] = new("Op6D",              0x428a10, Stub),
            [0x6e] = new("Op6E",              0x428c90, Stub),
            [0x6f] = new("Op6F",              0x428d80, Stub),
            [0x70] = new("Op70",              0x4292b0, Stub),
            [0x71] = new("Op71",              0x4293f0, Stub),
            [0x72] = new("Op72",              0x428520, Stub),
            [0x73] = new("Op73",              0x421a50, Stub),
            [0x74] = new("Op74",              0x421e10, Stub),
            [0x75] = new("Op75",              0x4222a0, Stub),
            [0x76] = new("Op76",              0x4225d0, Stub),
            [0x77] = new("Op77",              0x41be60, Stub),
            [0x78] = new("Op78",              0x41bde0, Stub),
            [0x79] = new("Op79",              0x422270, Stub),
        };

        // ===================== handlers reconstruidos =====================

        /// <summary>FUN_0041ee00: entra no canal (valida GroupId na lista de canais).</summary>
        private static void Op_EnterChannel(HandlerContext ctx)
        {
            var u = ctx.User;
            int idx = ctx.World.Channels.FindIndex(c => c.Id == u.GroupId);
            if (idx < 0)
            {
                Log.Warn("lobby", "[{0}] canal {1} inexistente -> DISC 7", u.Slot, u.GroupId);
                u.Disconnect(7);
                return;
            }
            u.Status = ctx.World.Channels[idx].Special ? UserStatus.LobbyGm : UserStatus.Lobby;
            Log.Info("lobby", "[{0}] entrou no canal {1} (status={2})", u.Slot, u.GroupId, u.Status);
            // reply subtype 1: [u16 1][u8 0]
            using var w = new PacketWriter();
            w.WriteWord(1).WriteByte(0);
            u.SendLobby(w.ToArray());
        }

        /// <summary>FUN_0041ef00: info do mundo/lobby (requer status >= Lobby).</summary>
        private static void Op_RequestWorldInfo(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status < UserStatus.Lobby)
            {
                u.Disconnect(8);
                return;
            }
            // reply subtype 2: [u16 2][u8 serverClosed][u16 ?][u16 ?][u32 ?][u32 ?][charname\0][sessionname\0][20x00]
            using var w = new PacketWriter();
            w.WriteWord(2);
            w.WriteByte(ctx.World.Locked ? 1 : 0);
            w.WriteWord(0).WriteWord((int)ctx.World.Channels.Count); // campos de config do mundo (aprox.)
            w.WriteInt32(0).WriteInt32(0);
            w.WriteCString(u.CharName);
            w.WriteCString(u.UserId);
            w.WriteBytes(new byte[0x14]);
            u.SendLobby(w.ToArray());
            Log.Info("lobby", "[{0}] world info enviado", u.Slot);
        }

        /// <summary>FUN_0041f060: GM abre/fecha o servidor (requer status GM).</summary>
        private static void Op_GmServerOpenClose(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status != UserStatus.LobbyGm)
            {
                u.Disconnect(9);
                return;
            }
            byte flag = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;
            if (flag == 0)
            {
                if (ctx.World.Locked) { ctx.World.SetLocked(false); Log.Info("gm", "[{0}] Server Open", u.Slot); }
            }
            else
            {
                if (!ctx.World.Locked)
                {
                    ctx.World.SetLocked(true);
                    Log.Info("gm", "[{0}] Server Close — desconectando nao-GM", u.Slot);
                    ctx.World.DisconnectNonGm(10);
                }
            }
            using var w = new PacketWriter();
            w.WriteWord(3).WriteByte(flag);
            u.SendLobby(w.ToArray());
        }

        /// <summary>
        /// Gate de estado dos handlers ainda nao totalmente reconstruidos, extraido do exe:
        /// (requer in-field, requer field-secondary, razao DISC se o gate falha). O gate
        /// (in-field/field-sec) e fiel; a razao DISC e best-effort (1o DISC do handler).
        /// </summary>
        private static readonly Dictionary<ushort, (bool InF, bool FSec, byte Disc)> StubGates = new()
        {
            [0x1b]=(true,true,0x2b),[0x1c]=(true,true,0x2c),[0x2d]=(true,true,0),[0x2e]=(true,true,0x36),
            [0x2f]=(true,true,0),[0x34]=(true,false,0x45),[0x36]=(true,true,0x46),[0x3a]=(true,true,0x50),
            [0x3b]=(true,true,0x52),[0x3d]=(true,true,0x60),[0x3e]=(true,true,0x62),[0x3f]=(true,true,0x65),
            [0x40]=(true,true,0x67),[0x41]=(true,true,0x6a),[0x42]=(true,true,0x73),[0x43]=(true,true,0x77),
            [0x45]=(true,true,0x7a),[0x46]=(true,true,0),[0x4a]=(true,true,0x83),[0x4b]=(true,true,0x86),
            [0x4c]=(true,true,0x89),[0x4d]=(true,true,0x8d),[0x4f]=(true,true,0x8f),[0x50]=(true,true,0x93),
            [0x53]=(true,true,0),[0x56]=(true,true,0xa1),[0x57]=(true,true,0xa3),[0x59]=(true,true,0xa6),
            [0x5a]=(true,true,0xa8),[0x5b]=(true,true,0xaa),[0x5d]=(true,true,0),[0x5e]=(true,true,0),
            [0x60]=(true,true,0xb1),[0x62]=(true,true,0),[0x65]=(true,false,0xbb),[0x6b]=(true,true,0xc2),
            [0x6c]=(true,true,0xc3),[0x6d]=(true,true,0xc4),[0x6e]=(true,true,0xd0),[0x6f]=(true,true,0xd3),
            [0x70]=(true,true,0),[0x71]=(true,true,0),[0x72]=(true,true,0xd6),[0x73]=(true,true,0),
            [0x74]=(true,true,0),[0x75]=(true,true,0xe7),[0x76]=(true,true,0),[0x78]=(true,false,0),
        };

        /// <summary>
        /// Handler ainda nao totalmente reconstruido: aplica o gate de estado (fiel ao exe)
        /// e loga. O corpo (parse + regra + resposta) e o que falta desenvolver — ver o
        /// FUN_xxxx citado na tabela e ghidra-proj/handlers.out.txt.
        /// </summary>
        private static void Stub(HandlerContext ctx)
        {
            var e = Table[ctx.Opcode];
            if (StubGates.TryGetValue(ctx.Opcode, out var g))
            {
                bool ok = (!g.InF || ctx.User.InField) && (!g.FSec || ctx.User.FieldSecondary);
                if (!ok)
                {
                    if (g.Disc != 0) ctx.User.Disconnect(g.Disc);
                    return;
                }
            }
            Log.Debug("op", "[{0}] {1} (FUN_{2:x}) — gate OK; corpo a reconstruir ({3} bytes)",
                ctx.User.Slot, e.Name, e.Addr, ctx.Raw.Length);
        }
    }
}
