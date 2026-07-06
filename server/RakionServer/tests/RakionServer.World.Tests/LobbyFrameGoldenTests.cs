using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test dos frames da cadeia lobby->canal->sala->stage SINTETIZADOS por <see cref="LobbyFrames"/>.
    /// Os builders foram cravados da decompilação do worldserv (objdump): cada frame é emitido com seu LEN
    /// REAL (3º arg do send) + zero-pad até o bloco de 12B. Os bytes além do LEN real, na captura, eram LIXO
    /// DE STACK — VARIAM entre sessões (provado por diff do golden vs mitm_full_113423.log), logo NÃO são
    /// replicados. As CONSTANTES e o DADO DE SESSÃO (userid 999=0x03e7, nome "JP", stage 432s) são exigidos
    /// byte-a-byte contra a captura do original. NENHUM frame de lobby carrega handle de sessão na cauda: a
    /// volta-à-lista pós-clear re-manda os MESMOS 0x1f/0x1e/0x36 da entrada (mitm_move_133859 l.460/461 ==
    /// l.19/20). <see cref="Synth_Is_Domain_Sourced"/> prova que userid/nome vêm do domínio (não é blob fixo).
    /// </summary>
    public class LobbyFrameGoldenTests
    {
        private const ushort RefUserId = 999;                       // 0x03e7 -> e7 03
        private const string RefName = "JP";                        // 0x4a 0x50
        private const int RefStageSec = 432;                        // RemainingSec = 435 = 0x01b3

        private static string Hex(byte[] b) => System.Convert.ToHexString(b).ToLowerInvariant();

        // ---- Frames 100% constante/domínio: idênticos à captura ORIGINAL (validação vs wire real) ----

        [Fact]
        public void GameGuard_MatchesOriginal() =>
            Assert.Equal("10004e95dd29ce3a55db20b6ad97a65cc01c000000000000", Hex(LobbyFrames.GameGuard()));

        [Fact]
        public void ChannelChat_SubtypeSlotTextNul() =>   // [22 00][chanSlot][texto\0] — worldserv FUN_0041bca0
            Assert.Equal("2200054865726f6932203a206f6900",   // slot 5, "Heroi2 : oi\0"
                Hex(LobbyFrames.ChannelChat(5, "Heroi2 : oi")));

        [Fact]
        public void ChannelChat_CapsTextAt0x80()
        {
            byte[] f = LobbyFrames.ChannelChat(0, new string('a', 200));
            Assert.Equal(2 + 1 + 0x80 + 1, f.Length);          // subtype(2)+slot(1)+texto(0x80)+nul(1)
        }

        [Fact]
        public void RemainingTime_RealLen9_ZeroPad() =>   // RE FUN_00408440: 9 bytes reais (tempo+best-players) + pad (era 00a00f lixo)
            Assert.Equal("480001b30100001414000000", Hex(LobbyFrames.RemainingTime(RefStageSec)));

        [Fact]
        public void MatchEnd_MatchesOriginal() =>
            Assert.Equal("440002000100000061736464", Hex(LobbyFrames.MatchEnd(2, "asdd")));

        [Fact]
        public void Endpoints_LocalDefault_MatchesOriginal() =>
            Assert.Equal("0e00007f00000108fd7f00000108fd000000000000000000",
                Hex(LobbyFrames.Endpoints(new byte[] { 127, 0, 0, 1 }, 2301)));

        // ---- Acks curtos cravados da decompilação: LEN real + zero-pad (cauda capturada era lixo de stack) ----

        [Fact]
        public void SpawnAck_RealLen3_ZeroPad() =>           // RE FUN_0041fef0: LEN=3 [14 00][status]; resto era lixo
            Assert.Equal("140000000000000000000000", Hex(LobbyFrames.SpawnAck()));

        [Fact]
        public void GameListEmpty_RealLen3_ZeroPad() =>      // RE FUN_00422c90/@0x41c0b7: lista vazia (solo) = LEN=3 [36 00][00]
            Assert.Equal("360000000000000000000000", Hex(LobbyFrames.GameListEmpty()));

        [Fact]
        public void RoomCreateAck_RealLen5_ZeroPad() =>      // RE FUN_00423580: LEN=5 [3b 00][status][seat]; resto era lixo
            Assert.Equal("3b0000000000000000000000", Hex(LobbyFrames.RoomCreateAck()));

        [Fact]
        public void MatchStartAck_StatusZero_BodyPendingRE() =>   // status 0; body zerado (semântica per-byte pendente de RE; não afeta o observador)
            Assert.Equal("430000000000000000000000", Hex(LobbyFrames.MatchStartAck()));

        [Fact]
        public void StageEndResult_Clear_RealLen6_ZeroPad() =>   // RE FUN_00405a90: 2bd=2 (clear) -> 6 bytes + padding
            Assert.Equal("4a0002010100000000000000", Hex(LobbyFrames.StageEndResult(2)));

        [Fact]
        public void StageEndResult_Death_RealLen6_ZeroPad() =>   // RE GameDiePlayer FUN_004087d0: 2bd=1 (morte) -> mesma forma, byte[2]=1
            Assert.Equal("4a0001010100000000000000", Hex(LobbyFrames.StageEndResult(1)));

        // ---- Frames com registro de player (entrada E volta-à-lista): LEN real + zero-pad (cauda era lixo) ----

        [Fact]
        public void SessionInfo_FullName_MatchesOriginalCapture() =>
            // RE FUN_00404fc0 + captura (uid6 "JP2" classe1): [1f 00][00 00][uid][nome COMPLETO\0][class][team][u32].
            // Nome COMPLETO (o WriteName de 2 bytes cortava "Heroi2"→"He" na identidade da sessão/messenger).
            Assert.Equal("1f000000" + "0600" + "4a503200" + "01" + "00" + "00000000",
                Hex(LobbyFrames.SessionInfo(6, "JP2", 1)));

        [Fact]
        public void SessionInfo_LongName_NotTruncated() =>
            Assert.Contains("4865726f693200", Hex(LobbyFrames.SessionInfo(1, "Heroi2", 1)));   // "Heroi2\0" inteiro

        [Fact]
        public void ChannelList_OneUser_MatchesOriginalCapture()
        {
            // Captura orig_capture2 (S>C 0x1e ao logar, 1 user "JP2" uid6 slot0 classe1): [1e 00][type][count]
            // ["dchannel01\0"][str2\0] + [slotIdx 1B][uid u16][nome COMPLETO\0][classe][time][u32]. A cauda de
            // lixo após o LEN real não é replicada.
            var users = new[] { new LobbyFrames.UserListEntry(0, 6, "JP2", 1) };
            Assert.Equal("1e000001646368616e6e656c303100000006004a503200010000000000",
                Hex(LobbyFrames.ChannelList(users)));
        }

        [Fact]
        public void ChannelList_ManyUsers_SlotIdxIsOneByte_WithFullNames()
        {
            // 2 users: o slotIdx é 1 BYTE (u16 desalinhava o parse do 2º registro no cliente).
            var users = new[]
            {
                new LobbyFrames.UserListEntry(3, 6, "Heroi2", 1),
                new LobbyFrames.UserListEntry(9, 7, "oHeroi", 2),
            };
            byte[] f = LobbyFrames.ChannelList(users);
            Assert.Equal(0x02, f[3]);                                   // count = 2
            Assert.Contains("030600" + "4865726f693200", Hex(f));       // [slot 03][uid 0600]"Heroi2\0"
            Assert.Contains("090700" + "6f4865726f6900", Hex(f));       // [slot 09][uid 0700]"oHeroi\0"
        }

        [Fact]
        public void ChannelUserRemove_Is0x20WithSlotIdx() =>
            Assert.Equal("200007", Hex(LobbyFrames.ChannelUserRemove(7)));

        // ---- Prova de que userid/nome vêm do DOMÍNIO (síntese, não blob cravado nem handle de captura) ----

        [Fact]
        public void Synth_Is_Domain_Sourced()
        {
            var a = Hex(LobbyFrames.SessionInfo(RefUserId, RefName));        // 999 + "JP"
            var b = Hex(LobbyFrames.SessionInfo(1234, "ZZ"));               // 0x04d2 + "ZZ"
            Assert.Contains("e7034a50", a);    // userid 999 (e703) + nome "JP" (4a50)
            Assert.Contains("d2045a5a", b);    // userid 1234 (d204) + nome "ZZ" (5a5a)
            Assert.NotEqual(a, b);             // entradas distintas -> frames distintos (não é blob fixo)
            var users = new[] { new LobbyFrames.UserListEntry(0, 1234, "ZZ", 0) };
            Assert.Contains("646368616e6e656c3031", Hex(LobbyFrames.ChannelList(users))); // "dchannel01" é const
        }

        // ---- 0x72 FieldInvitation (invite da sala): layout cravado do parse do cliente (engine.dll FUN_36193f40) ----

        [Fact]
        public void FieldInvitation_ByteExact_MatchesClientParse()
        {
            // Entrada: inviter id 5 "Go", sala 0x0122, map 3, mode 1, lvl 1..40, rounds 3, "Sala1", sem senha.
            // Layout EXATO que o cliente lê (FUN_36193f40): [72 00][id][nome\0][slot][map,mode,min,max,0,rounds,0]
            // [nome-sala\0][senha\0]. O u16 do slot cai LOGO após o NUL do nome (o parse usa strlen do nome).
            byte[] f = LobbyFrames.FieldInvitation(5, "Go", 0x0122, 3, 1, 1, 40, 3, "Sala1", "");
            Assert.Equal("72000500476f0022010301012800030053616c61310000", Hex(f));
        }

        [Fact]
        public void FieldInvitation_SlotFollowsNameNul_LongName()
        {
            // Nome maior desloca o slot/atributos: o cliente reancorra pelo NUL do nome (strlen), então o
            // fieldSlot tem que vir imediatamente após o \0 do inviterName — provado por Contains do bloco.
            byte[] f = LobbyFrames.FieldInvitation(7, "GoHeroi", 0x0200, 5, 2, 10, 30, 2, "Arena", "pw");
            // "GoHeroi\0" = 476f4865726f6900 ; depois [00 02](slot 0x0200) [05 02 0a 1e 00 02 00] ; "Arena\0" ; "pw\0"
            Assert.Contains("476f4865726f6900" + "0002" + "05020a1e000200" + "4172656e6100" + "707700", Hex(f));
        }

        [Fact]
        public void FieldInvitation_IsDomainSourced()
        {
            // Convidador/sala distintos -> frames distintos (síntese do Field, não blob fixo).
            var a = Hex(LobbyFrames.FieldInvitation(5, "Go", 0x0122, 3, 1, 1, 40, 3, "Sala1", ""));
            var b = Hex(LobbyFrames.FieldInvitation(9, "Ze", 0x0300, 4, 3, 5, 50, 1, "Outra", "x"));
            Assert.NotEqual(a, b);
            Assert.StartsWith("7200", a);                 // opcode 0x72
            Assert.Contains("0500" + "476f00", a);        // id 5 + "Go\0"
            Assert.Contains("0900", b);                   // id 9
        }
    }
}
