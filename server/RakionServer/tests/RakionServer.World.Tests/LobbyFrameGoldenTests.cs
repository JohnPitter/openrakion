using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test dos frames da cadeia lobby->canal->sala->stage SINTETIZADOS por <see cref="LobbyFrames"/>.
    /// As CONSTANTES e o DADO DE SESSÃO (userid 999=0x03e7, nome "JP", stage 432s) são exigidos byte-a-byte
    /// contra a captura do worldserv ORIGINAL (mitm A/B/C). Os HANDLES (ponteiros autorais que o cliente só
    /// ecoa) entram por parâmetro: aqui injeta-se o handle da sessão A (648c0509) para reproduzir a ESTRUTURA;
    /// em produção cada sessão gera o seu (nenhum byte é replay de uma sessão gravada). Os testes
    /// <see cref="Handle_Is_Sourced_Not_Baked"/> e <see cref="Constants_Are_Invariant_To_Handle"/> provam isso.
    /// </summary>
    public class LobbyFrameGoldenTests
    {
        private const ushort RefUserId = 999;                       // 0x03e7 -> e7 03
        private const string RefName = "JP";                        // 0x4a 0x50
        private const int RefStageSec = 432;                        // RemainingSec = 435 = 0x01b3
        private static readonly byte[] Fh = { 0x64, 0x8c, 0x05, 0x09 }; // handle da sessão A (p/ reproduzir a estrutura)

        private static string Hex(byte[] b) => System.Convert.ToHexString(b).ToLowerInvariant();

        // ---- Frames 100% constante/domínio: idênticos à captura ORIGINAL (validação vs wire real) ----

        [Fact]
        public void GameGuard_MatchesOriginal() =>
            Assert.Equal("10004e95dd29ce3a55db20b6ad97a65cc01c000000000000", Hex(LobbyFrames.GameGuard()));

        [Fact]
        public void GameListArmExtra_MatchesOriginal() =>
            Assert.Equal("3600006a4dcaaf2b4b3d9fa5", Hex(LobbyFrames.GameListArmExtra()));

        [Fact]
        public void RemainingTime_MatchesOriginal() =>
            Assert.Equal("480001b3010000141400a00f", Hex(LobbyFrames.RemainingTime(RefStageSec)));

        [Fact]
        public void ChannelList_Clear_MatchesOriginal() =>   // saída pós-clear: const + userid (sem handle)
            Assert.Equal("1e000001646368616e6e656c3031000000e7034a500001000000000030002503e7030000",
                Hex(LobbyFrames.ChannelList(RefUserId, RefName, Fh, clear: true)));

        [Fact]
        public void MatchEnd_MatchesOriginal() =>
            Assert.Equal("440002000100000061736464", Hex(LobbyFrames.MatchEnd(2, "asdd")));

        [Fact]
        public void Endpoints_LocalDefault_MatchesOriginal() =>
            Assert.Equal("0e00007f00000108fd7f00000108fd000000000000000000",
                Hex(LobbyFrames.Endpoints(new byte[] { 127, 0, 0, 1 }, 2301)));

        // ---- Frames com HANDLE: const/domínio vs original, handle = Fill(handle injetado) ----

        [Fact]
        public void SpawnAck_Structure() =>                  // [14 00][00 00][20000000][handle] — handle=Fh reproduz A
            Assert.Equal("1400000020000000648c0509", Hex(LobbyFrames.SpawnAck(Fh)));

        [Fact]
        public void GameListArm_Structure() =>
            Assert.Equal("3600000020000000648c0509", Hex(LobbyFrames.GameListArm(Fh)));

        [Fact]
        public void SessionInfo_Entry_Structure() =>
            Assert.Equal("1f000000e7034a500001000000000064e703000000000000",
                Hex(LobbyFrames.SessionInfo(RefUserId, RefName, Fh, clear: false)));

        [Fact]
        public void SessionInfo_Clear_Structure() =>
            Assert.Equal("1f000000e7034a50000100000000000200648c0509648c05",
                Hex(LobbyFrames.SessionInfo(RefUserId, RefName, Fh, clear: true)));

        [Fact]
        public void ChannelList_Entry_Structure() =>
            Assert.Equal("1e000001646368616e6e656c3031000000e7034a5000010000000000648c0509648c0509",
                Hex(LobbyFrames.ChannelList(RefUserId, RefName, Fh, clear: false)));

        [Fact]
        public void RoomCreateAck_Structure() =>
            Assert.Equal("3b00000000648c0509648c05", Hex(LobbyFrames.RoomCreateAck(Fh)));

        [Fact]
        public void MatchStartAck_Structure() =>
            Assert.Equal("430000648c0509643b000000", Hex(LobbyFrames.MatchStartAck(Fh)));

        [Fact]
        public void StageClearResult_Structure() =>
            Assert.Equal("4a0002010100648c0509648c", Hex(LobbyFrames.StageClearResult(Fh)));

        [Fact]
        public void GameListRefresh_Structure() =>
            Assert.Equal("360000648c05090002000000", Hex(LobbyFrames.GameListRefresh(Fh)));

        // ---- Provas de que o handle vem do DOMÍNIO (não é replay cravado) ----

        [Fact]
        public void Handle_Is_Sourced_Not_Baked()
        {
            var a = Hex(LobbyFrames.SpawnAck(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd }));
            var b = Hex(LobbyFrames.SpawnAck(new byte[] { 0x11, 0x22, 0x33, 0x44 }));
            Assert.EndsWith("aabbccdd", a);   // o handle injetado aparece no fio
            Assert.EndsWith("11223344", b);
            Assert.NotEqual(a, b);            // handles distintos -> frames distintos (logo, não é blob fixo)
        }

        [Fact]
        public void Constants_Are_Invariant_To_Handle()
        {
            var a = Hex(LobbyFrames.SpawnAck(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd }));
            var b = Hex(LobbyFrames.SpawnAck(new byte[] { 0x11, 0x22, 0x33, 0x44 }));
            Assert.StartsWith("1400000020000000", a);   // opcode + arma-scoring constantes p/ qualquer handle
            Assert.StartsWith("1400000020000000", b);
        }
    }
}
