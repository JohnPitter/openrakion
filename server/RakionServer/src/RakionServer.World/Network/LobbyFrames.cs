using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Database;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Síntese dos frames W->C da cadeia LOBBY -> CANAL -> SALA -> STAGE. Funções PURAS
    /// (parâmetros -> bytes): sem estado de sessão e sem I/O, montadas com <see cref="PacketWriter"/>
    /// a partir da ESTRUTURA documentada + CONSTANTES DE PROTOCOLO + DADO DE SESSÃO por parâmetro.
    /// Não há blob de captura: nenhum byte é replay de uma sessão gravada.
    ///
    /// REGRA-MESTRA (RE de worldserv.exe via objdump, 2026-06-18): a cifra de lobby empacota em
    /// BLOCOS DE 12 BYTES e o cliente lê cada frame pelo comprimento REAL que o servidor enviou
    /// (3º arg dos sends FUN_004038e0/FUN_0041b8a0/FUN_0041b940). Os bytes além desse LEN, dentro do
    /// bloco cifrado, são LIXO DE STACK do buffer de envio — VARIAM entre sessões (provado por diff do
    /// golden vs capture_field_entry/mitm_full_113423.log) e o cliente os ignora. Por isso TODO frame é
    /// emitido com seu LEN real + zero-pad até o bloco; nenhum "handle/token" capturado sobrevive.
    ///
    /// Builders cravados da decompilação (FUN @ endereço, LEN real):
    ///   0x10 GameGuard  : nonce de handshake estático (16B); GG neutralizado não valida.
    ///   0x14 CharacterSelectAck: FUN_0041fef0 LEN=3 [14 00][status].
    ///   0x1e ChannelList: FUN_00404da0  LEN var [1e 00][type][count][nome1\0][nome2\0][N registros]; solo=28.
    ///   0x1f SessionInfo: FUN_00404fc0  erro LEN=3 [1f 00][status]; ok LEN=15 [1f 00][00][00][uid:u16][registro].
    ///   0x25 SoloCreate : FUN_00418b00  LEN var [requestSeq:u16][25 00][status][nome/senha/descrição][config].
    ///   0x36 GameList   : FUN_00422c90 e FUN @0x41c0b7 (FieldPlayerList); LEN começa em 3 e cresce com a lista.
    ///                     Solo = lista vazia (count=0) = LEN=3 [36 00][00].
    ///   0x3b RoomCreate : FUN_00423580  LEN=5  [3b 00][status][seat:u16].
    ///   0x43 MatchStart : FUN_004079d0  LEN=3  [43 00][status] (status 0 = partida inicia).
    ///   0x48 Remaining  : FUN_00408440  LEN=9  [48 00][01][dur+3][2c0][2c1][best t1][best t2].
    ///   0x4a StageEnd   : clear=FUN_00405a90 (2bd=2) / morte=FUN_004087d0 (2bd=1)  LEN=6  [4a 00][2bd][2bf][2c0][2c1].
    /// Registro por-player de 0x1e/0x1f (FUN_0040afb0): [nome\0][class@1531][team@146c][dword@14d0].
    ///
    /// DADO DE SESSÃO: slot global, nome e presença do domínio; tempo restante do stage (duração da sala, dur+3).
    /// VOLTA-À-LISTA (pós-0x44/pós-clear): o original RE-MANDA os MESMOS 0x1f/0x1e/0x36 da entrada — confirmado
    /// em capture_field_entry/mitm_move_133859.log (l.460/461 == entrada l.19/20, só os bytes de lixo diferem).
    /// Não há frame "clear" distinto nem handle de sessão na cauda: tudo é o frame de entrada sintetizado.
    /// </summary>
    public static class LobbyFrames
    {
        /// <summary>Nome wire do canal padrão criado pelo World.</summary>
        public const string ChannelName = "channel01";

        /// <summary>0x10 GameGuard challenge (16B). IDÊNTICO em A=B=C -> constante (cliente GG-neutralizado
        /// não valida o conteúdo; é um nonce de handshake de forma fixa).</summary>
        private static readonly byte[] GameGuardChallenge =
            { 0x4e, 0x95, 0xdd, 0x29, 0xce, 0x3a, 0x55, 0xdb, 0x20, 0xb6, 0xad, 0x97, 0xa6, 0x5c, 0xc0, 0x1c };

        // ---- BUILDERS ----------------------------------------------------------------------------------

        /// <summary>0x10 GameGuard challenge: [10 00][nonce 16B][00 x6]. Tudo constante.</summary>
        public static byte[] GameGuard()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x10);
            w.WriteBytes(GameGuardChallenge);
            w.WriteBytes(new byte[6]);
            return w.ToArray();
        }

        /// <summary>Resposta lógica 0x14: [u16 opcode][u8 status]. O padding pertence à cifra.</summary>
        public static byte[] CharacterSelectAck(
            Database.CharacterSelectStatus status = Database.CharacterSelectStatus.Success)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x14);
            w.WriteByte((byte)status);
            return w.ToArray();
        }

        public static byte[] CharacterCreateAck(Database.CharacterCreateStatus status, int characterId)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x12);
            w.WriteByte((byte)status);
            if (status == Database.CharacterCreateStatus.Success)
                w.WriteInt32(characterId);
            return w.ToArray();
        }

        public static byte[] CharacterStateClearAck(Database.CharacterStateClearResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1b);
            w.WriteByte((byte)result.Status);
            if (result.Status == Database.CharacterStateClearStatus.Success)
            {
                w.WriteInt32(result.Cash);
                w.WriteWord(result.LevelPoint);
                w.WriteWord(result.PowerLevelPoint);
                WritePresents(w, result.Presents);
            }
            return w.ToArray();
        }

        public static byte[] CharacterRenameAck(Database.CharacterRenameResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1c);
            w.WriteByte((byte)result.Status);
            if (result.Status == Database.CharacterRenameStatus.Success)
            {
                w.WriteInt32(result.Cash);
                w.WriteCString(result.Name);
                WritePresents(w, result.Presents);
            }
            return w.ToArray();
        }

        public static byte[] InventoryEntitlementAck(
            Database.InventoryEntitlement entitlement, Database.EntitlementPurchaseResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(entitlement == Database.InventoryEntitlement.Bag ? 0x32 : 0x35);
            w.WriteByte((byte)result.Status);
            if (result.Status == Database.EntitlementPurchaseStatus.Success)
            {
                w.WriteInt32(result.Gold);
                w.WriteInt32(result.Cash);
                w.WriteByte(result.Value);
                w.WriteByte(result.CouponItemId != 0 ? 1 : 0);
                w.WriteWord(result.CouponItemId);
                WritePresents(w, result.Presents);
            }
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] PotionSlotPurchaseAck(Database.PotionSlotPurchaseResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x6f);
            w.WriteByte((byte)result.Status);
            if (result.Status == Database.EntitlementPurchaseStatus.Success)
            {
                w.WriteInt32(result.Gold);
                w.WriteInt32(result.Cash);
                w.WriteByte(result.PotionSlots);
                WritePresents(w, result.Presents);
            }
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] StageRankClearAck(Database.StageRankClearResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x70);
            w.WriteByte((byte)result.Status);
            if (result.Status == Database.StageEntitlementStatus.Success)
            {
                w.WriteInt32(result.Gold);
                w.WriteInt32(result.Cash);
                WritePresents(w, result.Presents);
            }
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] StageLevelFreeAck(Database.StageLevelFreeResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x71);
            w.WriteByte((byte)result.Status);
            if (result.Status == Database.StageEntitlementStatus.Success)
            {
                w.WriteInt32(result.Gold);
                w.WriteInt32(result.Cash);
                w.WriteUInt32(result.MinuteMarker);
                WritePresents(w, result.Presents);
            }
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] PowerUserPurchaseAck(Database.PowerUserPurchaseResult result)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x34).WriteByte((byte)result.Status);
            if (result.Status == Database.PowerUserPurchaseStatus.Success)
            {
                writer.WriteInt32(result.Gold);
                writer.WriteInt32(result.Cash);
                writer.WriteUInt32(result.PowerTimeMarker);
                writer.WriteWord(result.PowerLevelPoints);
                WritePresents(writer, result.Presents);
            }
            return writer.ToArray();
        }

        private static void WritePresents(PacketWriter writer, int[]? presents)
        {
            int count = System.Math.Min(4, presents?.Length ?? 0);
            writer.WriteByte(count);
            for (int i = 0; i < count; i++) writer.WriteInt32(presents![i]);
        }

        public static byte[] PresentNotification(int[] itemIds, string accountName)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x6a);
            int count = System.Math.Min(byte.MaxValue, itemIds.Length);
            w.WriteByte(count);
            for (int i = 0; i < count; i++) w.WriteInt32(itemIds[i]);
            w.WriteCString(accountName);
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] PresentPeekAck(Database.PresentPeekResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x6b);
            w.WriteByte((byte)result.Status);
            w.WriteInt32(result.PendingId);
            w.WriteInt32(result.ItemId);
            w.WriteWord(result.ItemOption);
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] PresentAcceptAck(Database.PresentAcceptResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x6c);
            w.WriteByte((byte)result.Status);
            w.WriteInt32(result.ResponseValue);
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] PresentDisposeAck(Database.PresentDisposeResult result)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x6d);
            w.WriteByte((byte)result.Status);
            w.WriteBytes(new byte[(12 - w.Length % 12) % 12]);
            return w.ToArray();
        }

        public static byte[] CharacterDeleteAck(byte status, ClanLoginSnapshot? clan = null)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x13);
            w.WriteByte(status);
            if (status == 0)
            {
                ClanLoginSnapshot snapshot = clan ?? ClanLoginSnapshot.Empty;
                w.WriteInt32(snapshot.Id);
                w.WriteByte(snapshot.Grade);
                w.WriteCString(snapshot.Name);
                w.WriteUInt32(snapshot.Rank);
                w.WriteWord(snapshot.Members);
                w.WriteUInt32(snapshot.Point);
                w.WriteUInt32(snapshot.MemberPoint);
                w.WriteUInt32(snapshot.MemberRank);
                w.WriteCString(snapshot.MasterCharacterName);
            }
            return w.ToArray();
        }

        public static byte[] BuddyNameAck(byte status, string buddyName)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x15);
            w.WriteByte(status);
            w.WriteCString(buddyName);
            return w.ToArray();
        }

        public static byte[] ClanMembers(IReadOnlyList<ClanMemberIdentity> members)
        {
            if (members.Count > 99)
                throw new ArgumentOutOfRangeException(nameof(members));
            using var writer = new PacketWriter();
            writer.WriteWord(0x78).WriteByte(0).WriteWord(members.Count);
            foreach (ClanMemberIdentity member in members)
            {
                if (Encoding.ASCII.GetByteCount(member.AccountName) > 16 ||
                    Encoding.ASCII.GetByteCount(member.BuddyName) > 12)
                    throw new ArgumentException("Identidade de clã excede o layout legado.", nameof(members));
                writer.WriteCString(member.AccountName).WriteCString(member.BuddyName);
            }
            return writer.ToArray();
        }

        public static byte[] ClanMembersStatus(byte status)
        {
            if (status is not (1 or 2))
                throw new ArgumentOutOfRangeException(nameof(status));
            using var writer = new PacketWriter();
            return writer.WriteWord(0x78).WriteByte(status).ToArray();
        }

        /// <summary>0x36 game-list (FieldPlayerList: FUN_00422c90 e o 2º builder @0x41c0b7). No original é a
        /// LISTA DE SALAS/PLAYERS de tamanho VARIÁVEL ([36 00][count][por entrada ...]); o LEN começa em 3 e
        /// só cresce com entradas. No mundo SOLO a lista está vazia -> count=0 -> LEN=3 = [36 00][00]. As caudas
        /// `20000000`/`648ce806`/`6a4dcaaf...` das 3 capturas (arm/extra/refresh) eram LIXO DE STACK distinto —
        /// não handle nem "token de arme": quem arma a game-list é o próprio count=0. 3 reais + zero-pad.</summary>
        public static byte[] GameListEmpty()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x36);
            w.WriteByte(0);                 // count = 0 (lista vazia no solo)
            w.WriteBytes(new byte[9]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        public static byte[] GameList(
            System.Collections.Generic.IReadOnlyList<Domain.RoomListSnapshot> fields)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x36);
            w.WriteByte((byte)System.Math.Min(fields.Count, 10));
            for (int i = 0; i < fields.Count && i < 10; i++)
                RoomListFrames.WriteEntry(w, fields[i]);
            int padding = (12 - (int)(w.Length % 12)) % 12;
            w.WriteBytes(new byte[padding]);
            return w.ToArray();
        }

        /// <summary>0x1f info de sessão/char. RE FUN_00404fc0: a ENTRADA (sucesso) tem LEN=15 =
        /// [1f 00][status][channelSlot][sessionSlot:u16][registro]. O registro (FUN_0040afb0) = [nome\0][class][team]
        /// [dword]. O byte 15+ do bloco era LIXO DE STACK — VARIOU entre sessões (06/64/08 no diff golden×log),
        /// logo zero-pad (LEN real=15). Userid e nome vêm do domínio. A volta-à-lista pós-clear re-manda ESTE
        /// MESMO frame (mitm_move_133859 l.460 == entrada l.19; só o byte de lixo difere — sem handle/marker).</summary>
        public static byte[] SessionInfo(
            byte channelSlot, ushort sessionSlot, ChannelPresenceRecord player)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1f);
            w.WriteByte(0);
            w.WriteByte(channelSlot);
            w.WriteWord(sessionSlot);
            WritePresence(w, player);
            PadLobbyBlock(w);
            return w.ToArray();
        }

        public static byte[] SessionInfo(ushort sessionSlot, string name) =>
            SessionInfo(0, sessionSlot, new ChannelPresenceRecord(name, 1, 0, 0));

        /// <summary>0x1e lista de canais. RE FUN_00404da0 e consumidor 0x361932a0:
        /// [1e 00][type][count][ownerSlot][nome1\0][nome2\0]
        /// [N registros de player]. No solo: type=0, count=1, 1 registro (slot global + nome + stats) -> LEN real=28.
        /// A cauda (bytes 28+) era LIXO DE STACK — VARIOU entre sessões (5e735fb8.../648c0509...), logo zero-pad.
        /// slot global e presença vêm do domínio. A volta-à-lista pós-clear re-manda ESTE MESMO frame (mitm_move l.461 ==
        /// entrada l.20, byte-a-byte).</summary>
        public static byte[] ChannelList(
            byte type, byte ownerSlot, string channelName, string password,
            IReadOnlyList<ChannelMemberRecord> members)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1e);
            w.WriteByte(type);
            w.WriteByte((byte)Math.Min(byte.MaxValue, members.Count));
            w.WriteByte(ownerSlot);
            w.WriteCString(channelName);
            w.WriteCString(password);
            for (int index = 0; index < members.Count && index < byte.MaxValue; index++)
            {
                ChannelMemberRecord member = members[index];
                w.WriteByte(member.ChannelSlot);
                w.WriteWord(member.SessionSlot);
                WritePresence(w, member.Player);
            }
            PadLobbyBlock(w);
            return w.ToArray();
        }

        public static byte[] ChannelList(ushort sessionSlot, string name) =>
            ChannelList(0, Channel.NoOwnerSlot, ChannelName, "", new[]
            {
                new ChannelMemberRecord(0, sessionSlot, new ChannelPresenceRecord(name, 1, 0, 0))
            });

        public static byte[] ChannelExit(byte channelSlot)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x20).WriteByte(channelSlot);
            PadLobbyBlock(writer);
            return writer.ToArray();
        }

        public static byte[] ChannelChat(byte channelSlot, string text)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x22).WriteByte(channelSlot);
            byte[] bytes = Encoding.ASCII.GetBytes(text ?? "");
            writer.WriteBytes(bytes.AsSpan(0, Math.Min(bytes.Length, 128)).ToArray());
            writer.WriteByte(0);
            PadLobbyBlock(writer);
            return writer.ToArray();
        }

        public static byte[] ChannelOwner(byte channelSlot)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x28).WriteByte(channelSlot);
            PadLobbyBlock(writer);
            return writer.ToArray();
        }

        /// <summary>0x3b ack de criação de sala. RE FUN_00423580 (@0x423580, linha 264): LEN=5 =
        /// [3b 00][status=0][seat:u16] (seat = slot do field-objeto, 0 no solo). O [538b003600007f] do blob
        /// antigo era LIXO DE STACK. 5 reais + zero-pad.</summary>
        public static byte[] RoomCreateAck() => RoomCreateAck(0, 0);

        public static byte[] RoomCreateAck(ushort fieldId, byte status)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3b);
            w.WriteByte(status);
            w.WriteWord(fieldId);
            w.WriteBytes(new byte[7]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x43 ack de start do match. RE FUN_004079d0 (@0x4079d0): a resposta REAL tem LEN=3 =
        /// [43 00][status], enviada via FUN_0041b8a0(...,3,...). status 0 = partida inicia (seta o timer
        /// this+0x2b8 = tick+40000ms); 1/2/3 = não pôde iniciar (faltam players/prontidão). O [handle 5B]
        /// [3b 00 00 00] do blob antigo (e o [0003000000 3b000000] da captura) era LIXO DE STACK — varia
        /// entre sessões, não é handle nem opcode ecoado. 3 reais + zero-pad.</summary>
        public static byte[] MatchStartAck() => MatchStartAck(0);

        public static byte[] MatchStartAck(byte status)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x43);
            w.WriteByte(status);            // 0 inicia; 1/2/3 rejeitam conforme o gate do servidor
            w.WriteBytes(new byte[9]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x48 tempo restante do stage. RE FUN_00408440 (@0x408440, linha 1696): LEN=9 =
        /// [48 00][01][RemainingSec=dur+3 u16][this+0x2c0=0][this+0x2c1=0][this+0x122][this+0x123]. Os 2
        /// últimos são índices de best-player (14 14 na captura). O [a0 0f] final era LIXO DE STACK.
        /// RemainingSec vem do domínio (duração da sala). Referência: 432s -> 435 (0x01b3). 9 reais + zero-pad.</summary>
        public static byte[] RemainingTime(int durationSec)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x48);
            w.WriteByte(1);
            w.WriteWord(durationSec + 3);
            w.WriteWord(0);                         // this+0x2c0 / this+0x2c1
            w.WriteByte(0x14); w.WriteByte(0x14);   // this+0x122 / this+0x123 = índices best-player (LEN=9 acaba aqui)
            w.WriteBytes(new byte[3]);              // padding do bloco de 12B (era lixo de stack: 00 a0 0f)
            return w.ToArray();
        }

        /// <summary>0x4a resultado de FIM de stage (tela de resumo). RE de DOIS builders que montam a MESMA
        /// forma de 6 bytes [4a 00][this+0x2bd][this+0x2bf][this+0x2c0][this+0x2c1], diferindo só no this+0x2bd:
        ///  - CLEAR (FUN_00405a90 @0x405a90): 2bd=2 (eco do request StageClear) -> tela de Rank.
        ///  - MORTE (GameDiePlayer FUN_004087d0 @0x4087d0, modo survival/case 2): 2bd=1 + timer 15s -> resumo de fim.
        /// <paramref name="resultType"/> = this+0x2bd (2=clear, 1=morreu). Os 6 bytes seguintes do bloco de 12B
        /// eram LIXO DE STACK na captura -> zero-pad determinístico.</summary>
        public static byte[] StageEndResult(byte resultType)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x4a);
            w.WriteByte(resultType);                              // this+0x2bd (2=stage clear, 1=morte)
            w.WriteByte(0x01); w.WriteByte(0x01); w.WriteByte(0); // estado do field (this+0x2bf/0x2c0/0x2c1)
            w.WriteBytes(new byte[6]);                            // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        public static byte[] RoomJoinResult(byte status)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x38).WriteByte(status).WriteBytes(new byte[9]);
            return w.ToArray();
        }

        public static byte[] QuickJoinEmpty()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x39).WriteBytes(new byte[10]);
            return w.ToArray();
        }

        /// <summary>Resultado lógico 0x2C: status e marcador de tempo recebido no login.</summary>
        public static byte[] InventoryEnterAck(uint serverTimeMarker)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x2c);
            w.WriteByte(0);
            w.WriteUInt32(serverTimeMarker);
            return w.ToArray();
        }

        /// <summary>Resposta correlacionada de criação de stage solo. O callback do DB original
        /// (FUN_00418b00) envia [requestSeq][0x25][status] antes dos dados variáveis do stage.</summary>
        public static byte[] SoloStageCreateAck(
            ushort requestSequence, byte status, RoomCreationOptions options)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(requestSequence).WriteWord(0x25).WriteByte(status)
                .WriteCString(options.Name).WriteCString(options.Password)
                .WriteCString(options.Description).WriteByte(options.MapId)
                .WriteByte(options.Rounds).WriteWord(options.DurationSeconds)
                .WriteByte(options.FragLimit).WriteByte(options.MinLevel)
                .WriteByte(options.MaxLevel).WriteByte(options.LevelRangeCode);
            return writer.ToArray();
        }

        public static byte[] InventoryEnterResult(byte status)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x2c).WriteByte(status).WriteUInt32(0);
            return writer.ToArray();
        }

        /// <summary>Resultado lógico do 0x2D: opcode e status 0/1/2.</summary>
        public static byte[] InventoryLeaveResult(byte status)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x2d).WriteByte(status);
            return writer.ToArray();
        }

        /// <summary>Erro curto de compra/venda: [u16 opcode][u8 status].</summary>
        public static byte[] StorageMutationError(bool sale, byte status)
        {
            using var w = new PacketWriter();
            w.WriteWord(sale ? 0x2f : 0x2e).WriteByte(status);
            return w.ToArray();
        }

        /// <summary>Snapshot mínimo de sucesso da venda reconstruído de FUN_004215A0.</summary>
        public static byte[] StorageSaleAck(
            uint fieldId, uint fieldSecondary, ushort itemId, uint credit,
            byte slot, uint itemHandle, byte level, uint experience)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x15).WriteWord(0);
            w.WriteUInt32(fieldId).WriteUInt32(fieldSecondary);
            w.WriteWord(itemId).WriteUInt32(credit).WriteByte(slot);
            w.WriteUInt32(itemHandle).WriteByte(level).WriteUInt32(experience);
            w.WriteByte(0).WriteByte(0);
            return w.ToArray();
        }

        /// <summary>0x53 confirmação do resultado: status, stage, rank e slots reportados.</summary>
        public static byte[] StageResultAck(byte stage, byte rank, IReadOnlyList<ushort> mapSlots)
        {
            if (mapSlots == null || mapSlots.Count > byte.MaxValue)
                throw new ArgumentException("A lista de slots do resultado é inválida.", nameof(mapSlots));

            using var w = new PacketWriter();
            w.WriteWord(0x53);
            w.WriteByte(0);
            w.WriteByte(stage);
            w.WriteByte(rank);
            w.WriteByte(mapSlots.Count);
            foreach (ushort slot in mapSlots) w.WriteWord(slot);
            return w.ToArray();
        }

        /// <summary>0x44 fim de partida (volta ao game room): [44 00][reason][00][01 00 00 00][nome da sala].
        /// O nome vem do domínio (último campo, tamanho variável seguro).</summary>
        public static byte[] MatchEnd(byte reason, string roomName)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x44);
            w.WriteByte(reason);
            w.WriteByte(0);
            w.WriteUInt32(1);
            w.WriteBytes(Encoding.ASCII.GetBytes(roomName ?? ""));
            return w.ToArray();
        }

        /// <summary>0x0e OnRecvSuccessUDP: devolve os dois endpoints do cliente observados pelo World.
        /// Cada porta é big-endian. Captura dourada: MITM 51708/51709.</summary>
        public static byte[] Endpoints(IPEndPoint endpoint1, IPEndPoint endpoint2)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x0e);
            w.WriteByte(0);
            NetworkEndpointCodec.Write(w, endpoint1);
            NetworkEndpointCodec.Write(w, endpoint2);
            return w.ToArray();
        }

        private static void WritePresence(PacketWriter writer, ChannelPresenceRecord player)
        {
            writer.WriteCString(player.Name);
            writer.WriteByte(player.Class);
            writer.WriteByte(player.SubStatus);
            writer.WriteInt32(player.ClanId);
        }

        private static void PadLobbyBlock(PacketWriter writer)
        {
            int padding = (12 - (int)(writer.Length % 12)) % 12;
            writer.WriteBytes(new byte[padding]);
        }
    }

    public readonly record struct ChannelPresenceRecord(
        string Name, byte Class, byte SubStatus, int ClanId);

    public readonly record struct ChannelMemberRecord(
        byte ChannelSlot, ushort SessionSlot, ChannelPresenceRecord Player);
}
