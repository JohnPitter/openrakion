using RakionServer.Common;

namespace RakionServer.World.Network
{
    // GERADO pelo workflow multi-agente (rakion-handlers-reconstruct): os 53 handlers
    // de gameplay reconstruidos do decompile (handlers.out.txt) e verificados.
    // O static ctor repointa a Table (de Stub) para estes metodos.
    //
    // Handlers fatiados por dominio em arquivos partial:
    //   WorldHandlers.Generated.Field.cs      — field/combate/chat de campo (Op_Field*, Op_Game*, ping/relay)
    //   WorldHandlers.Generated.Room.cs       — Op_Room*, Op_BuyLotto, reward/emblem
    //   WorldHandlers.Generated.Shop.cs       — compra/venda/inventario/alocacao de pontos
    //   WorldHandlers.Generated.GameResult.cs — Op_GameResultReport
    //   WorldHandlers.Generated.Verify.cs     — Op_Verify*, Op_ServerInfoDump, Op_FieldStateQuery
    // Aqui ficam a tabela de dispatch e os handlers compartilhados/sem dominio definido.
    public static partial class WorldHandlers
    {
        static WorldHandlers() => RegisterGenerated();

        private static void RegisterGenerated()
        {
            Table[0x14] = new("FieldGameStart", 0x41fef0, Op_FieldGameStart);
            Table[0x1b] = new("FieldJoinById", 0x4208e0, Op_FieldJoinById);
            Table[0x1c] = new("FieldJoinByName", 0x420a40, Op_FieldJoinByName);
            Table[0x2d] = new("RoomRosterSync", 0x420f10, Op_RoomRosterSync);
            Table[0x2e] = new("RoomMemberQuery", 0x421210, Op_RoomMemberQuery);
            Table[0x33] = new("InventoryAllocationPoint", 0x4229f0, Op_InventoryAllocationPoint);
            Table[0x2f] = new("GroupMemberInfo", 0x4215a0, Op_GroupMemberInfo);
            Table[0x34] = new("GroupListQuery", 0x422b10, Op_GroupListQuery);
            Table[0x36] = new("FieldPlayerList", 0x422c90, Op_FieldPlayerList);
            Table[0x3a] = new("FieldLeaveGame", 0x4234e0, Op_FieldLeaveGame);
            Table[0x3b] = new("RoomCreate", 0x423580, Op_RoomCreate);
            // 0x3d/0x46/0x4d/0x4f: NAO sobrescrever — a tabela base (Build) aponta p/ as versoes
            // Recon (ReconCombatA/B/RoomB), integradas ao motor de partida (Field/wins/settle).
            // As versoes geradas da mesma FUN_xxxx nao mexem no estado do match (ex.: o 0x4d
            // gerado ignorava o golem destruido -> a partida terminava em empate por timeout).
            Table[0x3e] = new("FieldGameReady", 0x423b70, Op_FieldGameReady);
            Table[0x3f] = new("FieldGameStart_3f", 0x423c00, Op_FieldGameStart_3f);
            Table[0x40] = new("FieldSetGameMode", 0x423cc0, Op_FieldSetGameMode);
            Table[0x41] = new("FieldCreateRoomEntry", 0x423dd0, Op_FieldCreateRoomEntry);
            Table[0x42] = new("FieldUnitCommand", 0x424100, Op_FieldUnitCommand);
            Table[0x43] = new("FieldUnitStop", 0x424210, Op_FieldUnitStop);
            Table[0x45] = new("FieldUnitByteAction", 0x4242c0, Op_FieldUnitByteAction);
            Table[0x4a] = new("FieldUnitCharAction", 0x4246e0, Op_FieldUnitCharAction);
            Table[0x4b] = new("FieldChatBroadcast", 0x4247b0, Op_FieldChatBroadcast);
            Table[0x4c] = new("FieldTaggedBroadcast", 0x424880, Op_FieldTaggedBroadcast);
            // 0x50: NAO sobrescrever — vale Op_0x50_Recon (exp/gold + level-up; o gerado
            // Op_GamePointSettle desconectava com DISC 0x95 no settle pos-round do PvP).
            Table[0x53] = new("GameResultReport", 0x425010, Op_GameResultReport);
            Table[0x56] = new("GameChat", 0x425620, Op_GameChat);
            Table[0x57] = new("GameVoiceChat", 0x4256d0, Op_GameVoiceChat);
            Table[0x59] = new("GameEmoteAction", 0x4257b0, Op_GameEmoteAction);
            Table[0x5a] = new("GameWhisperToSlot", 0x425860, Op_GameWhisperToSlot);
            Table[0x5b] = new("FieldPlayerAction", 0x425990, Op_FieldPlayerAction);
            Table[0x5d] = new("FieldChatNamed", 0x425a70, Op_FieldChatNamed);
            Table[0x5e] = new("FieldChatCode", 0x425bb0, Op_FieldChatCode);
            Table[0x60] = new("FieldTargetCommand", 0x425cc0, Op_FieldTargetCommand);
            Table[0x61] = new("SetUserPing", 0x41c270, Op_SetUserPing);
            Table[0x62] = new("FieldRelayAction", 0x41c2b0, Op_FieldRelayAction);
            Table[0x64] = new("VerifyTutorialStage", 0x4283a0, Op_VerifyTutorialStage);
            Table[0x65] = new("VerifyClientHash", 0x428430, Op_VerifyClientHash);
            Table[0x6b] = new("RequestFieldTick", 0x4286a0, Op_RequestFieldTick);
            Table[0x6c] = new("RequestFieldSnapshot", 0x428750, Op_RequestFieldSnapshot);
            Table[0x6d] = new("FieldEmoteEcho", 0x428a10, Op_FieldEmoteEcho);
            Table[0x6e] = new("FieldUseItem", 0x428c90, Op_FieldUseItem);
            Table[0x6f] = new("RoomReadyEmblem", 0x428d80, Op_RoomReadyEmblem);
            Table[0x70] = new("RoomRankReward", 0x4292b0, Op_RoomRankReward);
            Table[0x71] = new("RoomFixedReward", 0x4293f0, Op_RoomFixedReward);
            Table[0x72] = new("RoomMemberFieldInfo", 0x428520, Op_RoomMemberFieldInfo);
            Table[0x73] = new("RoomCharSelectInfo", 0x421a50, Op_RoomCharSelectInfo);
            Table[0x74] = new("RoomMoveAction", 0x421e10, Op_RoomMoveAction);
            Table[0x75] = new("BuyLotto", 0x4222a0, Op_BuyLotto);
            Table[0x76] = new("RoomReadyState", 0x4225d0, Op_RoomReadyState);
            Table[0x77] = new("ServerInfoDump", 0x41be60, Op_ServerInfoDump);
            Table[0x78] = new("FieldStateQuery", 0x41bde0, Op_FieldStateQuery);
            Table[0x79] = new("DisconnectNotText", 0x422270, Op_DisconnectNotText);
        }

        private static void Op_GroupListQuery(HandlerContext ctx)
        {
            var u = ctx.User;
            // payload: [byte mode][byte hasExtra]( [u16 extra] se hasExtra!=0 )
            byte mode = ctx.P.Byte();      // cVar2 = *param_3
            byte hasExtra = ctx.P.Byte();  // cVar1 = param_3[1]
            ushort extra = 0;
            if (hasExtra != 0) extra = ctx.P.UInt16(); // *(ushort*)(param_3+2)

            // Guard: mode deve ser 0 ou 1 (else DISC 0x45) — sem checagem de field aqui
            if (mode != 0 && mode != 1) { u.Disconnect(0x45); return; }

            // TODO FUN_0040b2c0(user, mode, hasExtra, extra, out a(u32), out b(u32), out c(u32)) -> byte err
            byte err = 0;   // sucesso ((char)uVar4)
            uint a = 0;     // local_1010 -> local_ff8
            uint b = 0;     // local_1018 -> local_ff4
            uint c = 0;     // local_1014 -> local_ff0

            if (err != 0)
            {
                // Falha: SendLobby subtype 0x34 + [byte err] (len 3)
                using var wf = new PacketWriter();
                wf.WriteWord(0x34);   // local_1004 (subtype lobby)
                wf.WriteByte(err);    // local_1002 low = (char)uVar4
                u.SendLobby(wf.ToArray());
                return;
            }

            // Sucesso: SendMessage subtype 0x17 (local_1002 = 0x17) via FUN_0041b940.
            // local_1004 (off0) = *(0x1488) e o SEQ que SendMessage ja prepende -> NAO entra no payload.
            // Payload comeca em fieldId (offset 4).
            using var w = new PacketWriter();
            w.WriteUInt32((uint)u.FieldId);    // local_1000 (off4) = *(0x1460) fieldId
            w.WriteByte(mode);                 // local_ffc  (off8) = (byte)local_100c
            w.WriteWord((ushort)0);            // local_ffb  (off9) = *(0x2370) — TODO campo interno
            w.WriteByte(hasExtra);             // local_ff9  (off0xb) = cVar1
            if (hasExtra == 1)
            {
                w.WriteUInt32(a);              // local_ff8  (off0xc)
                w.WriteUInt32(b);              // local_ff4  (off0x10)
                w.WriteUInt32(c);              // local_ff0  (off0x14)
                w.WriteWord(extra);            // local_fec  (off0x18)
            }
            u.SendMessage(0x17, w.ToArray());
        }
    }
}
