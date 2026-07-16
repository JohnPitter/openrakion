using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Tabela de handlers de opcode do World Server — reconstrucao do switch de
    /// FUN_0042ab40 (worldserv.exe). Cada opcode aceito aponta diretamente para o
    /// handler final; a tabela não depende de sobrescritas posteriores.
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

        public static bool AcceptsOpcode(ushort opcode) => Table.ContainsKey(opcode);

        public static bool UsesStub(ushort opcode) =>
            Table.TryGetValue(opcode, out var entry) &&
            entry.Fn.Method.Name is "Stub" or "ReconStub";

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
            [0x01] = new("EnterChannel", 0x41ee00, Op_EnterChannel),
            [0x02] = new("RequestWorldInfo", 0x41ef00, Op_RequestWorldInfo),
            [0x03] = new("GmServerOpenClose", 0x41f060, Op_GmServerOpenClose),
            [0x04] = new("AdminBan", 0x41f1a0, Op_AdminBanEcho),
            [0x05] = new("AdminNotice", 0x41f290, Op_AdminNotice),
            [0x08] = new("GmSetVars", 0x429030, Op_GmSetVars),
            [0x09] = new("GmQueryEntry", 0x41f5c0, Op_GmQueryEntry),
            [0x0a] = new("GmGetVars", 0x429140, Op_GmGetVars),
            [0x0b] = new("GmSetClientMd5", 0x41f480, Op_GmSetClientMd5),
            [0x0e] = new("SuccessUdp", 0x41fa40, Op_SuccessUdp),
            [0x0f] = new("KeepAlive", 0x41fb30, Op_KeepAlive),
            [0x10] = new("GameGuardAuth", 0x41fc00, Op_GameGuardAuth),// log "[%04u] GMGD %u" + DISC 0x18
            [0x12] = new("CharacterCreate", 0x41fcd0, Op_CharacterCreate),
            [0x13] = new("CharacterDelete", 0x41fe10, Op_CharacterDelete),
            [0x14] = new("CharacterSelect", 0x41fef0, Op_CharacterSelect),
            [0x15] = new("CharacterChangeBuddyName", 0x420120, Op_CharacterChangeBuddyName),
            [0x16] = new("CharacterWhisper", 0x420200, Op_FieldWhisper),
            [0x17] = new("CharacterWhereAmI", 0x420410, Op_GetLocation),
            [0x18] = new("CharacterWhereAreYou", 0x420520, Op_FindUser),
            [0x19] = new("CharacterGetUserName", 0x420760, Op_CharacterGetUserName),
            [0x1a] = new("CharacterTutorialClear", 0x420840, Op_CharacterTutorialClear),
            [0x1b] = new("CharacterStateClear", 0x4208e0, Op_CharacterStateClear),
            [0x1c] = new("CharacterChangeCharName", 0x420a40, Op_CharacterChangeCharName),
            [0x1e] = new("ChannelCharacters", 0x429230, Op_ChannelCharacters),
            [0x20] = new("SessionCleanup", 0x41bc10, Op_SessionCleanup),
            [0x22] = new("ChannelChat", 0x41bca0, Op_ChannelChat),
            // --- field / sala / chat / jogo ---
            [0x29] = new("ChannelFieldPingRequest", 0x420c20, Op_ChannelFieldPingRequest),
            [0x2a] = new("ChannelFieldPingResponse", 0x420cb0, Op_ChannelFieldPingResponse),
            [0x2c] = new("InventoryEnter", 0x420de0, Op_InventoryEnter),
            [0x2d] = new("InventoryLeave", 0x420f10, Op_InventoryLeave),
            [0x2e] = new("InventoryBuy", 0x421210, Op_InventoryBuy),
            [0x2f] = new("InventorySell", 0x4215a0, Op_InventorySell),
            [0x31] = new("InventoryMove", 0x421870, Op_InventoryMove),
            [0x32] = new("InventoryBuyBag", 0x4226b0, Op_InventoryBuyBag),
            [0x33] = new("InventoryAllocationPoint", 0x4229f0, Op_InventoryAllocationPoint),
            [0x34] = new("InventoryBuyPowerUser", 0x422b10, Op_InventoryBuyPowerUser),
            [0x35] = new("InventoryBuyCharacterSlot", 0x422850, Op_InventoryBuyCharacterSlot),
            [0x36] = new("FieldList", 0x422c90, Op_FieldList),
            [0x38] = new("FieldEnter", 0x423100, Op_FieldEnter),
            [0x39] = new("FieldQuickEnter", 0x423300, Op_FieldQuickEnter),
            [0x3a] = new("FieldExit", 0x4234e0, Op_FieldExit),
            [0x3b] = new("FieldCreate", 0x423580, Op_FieldCreate),
            [0x3d] = new("FieldWeaponChange", 0x423ad0, Op_0x3D_Recon),
            [0x3e] = new("FieldChangeTeam", 0x423b70, Op_0x3E_Recon),
            [0x3f] = new("FieldClose", 0x423c00, Op_0x3F_Recon),
            [0x40] = new("FieldKick", 0x423cc0, Op_0x40_Recon),
            [0x41] = new("FieldChangeRule", 0x423dd0, Op_0x41_Recon),
            [0x42] = new("FieldChangeSlotStatus", 0x424100, Op_0x42_Recon),
            [0x43] = new("FieldGameStart", 0x424210, Op_0x43_Recon),
            [0x45] = new("FieldGameEnter", 0x4242c0, Op_0x45_Recon),
            [0x46] = new("FieldGameExit", 0x424350, Op_0x46_Recon),
            [0x47] = new("FieldChat", 0x4244f0, Op_FieldChat),
            [0x48] = new("FieldGameRoundStart", 0x424640, Op_FieldGameRoundStart),
            [0x4a] = new("FieldGameRoundEnd", 0x4246e0, Op_0x4A_Recon),
            [0x4b] = new("FieldGameAddPlayer", 0x4247b0, Op_0x4B_Recon),
            [0x4c] = new("FieldGameAddPlayerReply", 0x424880, Op_0x4C_Recon),
            [0x4d] = new("FieldGameMasterGolem", 0x424980, Op_0x4D_Recon),
            [0x4f] = new("FieldGameDiePlayer", 0x424a20, Op_0x4F_Recon),
            [0x50] = new("Op_0x50_Recon", 0x424b60, Op_0x50_Recon),  // GameResult/scoring
            [0x53] = new("FieldGameStagePoint", 0x425010, Op_FieldGameStagePoint),
            [0x61] = new("WorldEchoReply", 0x41c270, Op_WorldEchoReply),
            [0x56] = new("FieldGameTunnelingAll", 0x425620, Op_FieldGameTunnelingAll),
            [0x57] = new("FieldGameTunnelingOne", 0x4256d0, Op_FieldGameTunnelingOne),
            [0x59] = new("FieldPingRequest", 0x4257b0, Op_FieldPingRequest),
            [0x5a] = new("FieldPingResponse", 0x425860, Op_FieldPingResponse),
            [0x5b] = new("FieldForceChangeTeam", 0x425990, Op_FieldForceChangeTeam),
            [0x5d] = new("FieldGameVoteOpen", 0x425a70, Op_FieldVoteOpen),
            [0x5e] = new("FieldGameVote", 0x425bb0, Op_FieldVoteCast),
            [0x60] = new("FieldBossTargetReport", 0x425cc0, Op_FieldBossTargetReport),
            [0x62] = new("FieldSlotUdp", 0x41c2b0, Op_FieldRelayAction),
            [0x64] = new("GmOperation", 0x4283a0, Op_GmOperationIpGate),
            [0x65] = new("ChCode", 0x428430, Op_VerifyClientHash),
            [0x6b] = new("PresentPeek", 0x4286a0, Op_PresentPeek),
            [0x6c] = new("PresentAccept", 0x428750, Op_PresentAccept),
            [0x6d] = new("PresentDispose", 0x428a10, Op_PresentDispose),
            [0x6e] = new("FieldGamePotion", 0x428c90, Op_FieldUseItem),
            [0x6f] = new("InventoryBuyPotionSlot", 0x428d80, Op_InventoryBuyPotionSlot),
            [0x70] = new("InventoryBuyStageRankClear", 0x4292b0, Op_InventoryBuyStageRankClear),
            [0x71] = new("InventoryBuyStageLevelFree", 0x4293f0, Op_InventoryBuyStageLevelFree),
            [0x72] = new("FieldInvitation", 0x428520, Op_FieldInvitation),
            [0x73] = new("InventoryStackPotion", 0x421a50, Op_InventoryStackPotion),
            [0x74] = new("EnchantReinforce", 0x421e10, Op_EnchantPreview),
            [0x75] = new("BuyLotto", 0x4222a0, Op_BuyLotto),
            [0x76] = new("AskLotto", 0x4225d0, Op_AskLotto),
            [0x77] = new("ServerInfoDump", 0x41be60, Op_ServerInfoDump),
            [0x78] = new("ClanMembersQuery", 0x41bde0, Op_ClanMembersQuery),
            [0x79] = new("DisconnectNotText", 0x422270, Op_DisconnectNotText),
        };

        // ===================== handlers reconstruidos =====================

        /// <summary>FUN_0041ee00: entra no canal (valida GroupId na lista de canais).</summary>
        private static void Op_EnterChannel(HandlerContext ctx)
        {
            var u = ctx.User;
            int idx = ctx.World.Groups.FindIndex(group => group.Id == u.GroupId);
            if (idx < 0)
            {
                Log.Warn("lobby", "[{0}] canal {1} inexistente -> DISC 7", u.Slot, u.GroupId);
                u.Disconnect(7);
                return;
            }
            u.Status = GmAuthorization.LobbyStatus(u.Authority,
                ctx.World.Config.GmEnabled, ctx.World.Groups[idx].Special);
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
            w.WriteWord(0).WriteWord((int)ctx.World.Groups.Count); // campos de config do mundo (aprox.)
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
            if (!u.CanExecuteGm(GmPermission.ServerLock))
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

    }
}
