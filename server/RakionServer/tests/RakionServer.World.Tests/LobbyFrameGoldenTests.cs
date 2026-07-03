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
        public void SessionInfo_RealLen15_ZeroPad() =>  // RE FUN_00404fc0: ok=LEN15 [1f 00][00][00][uid][registro]; byte15+ lixo
            Assert.Equal("1f000000e7034a5000010000000000000000000000000000",
                Hex(LobbyFrames.SessionInfo(RefUserId, RefName)));

        [Fact]
        public void ChannelList_RealLen28_ZeroPad() =>  // RE FUN_00404da0: solo=LEN28 [1e..][nome][registro]; bytes28+ lixo
            Assert.Equal("1e000001646368616e6e656c3031000000e7034a50000100000000000000000000000000",
                Hex(LobbyFrames.ChannelList(RefUserId, RefName)));

        // ---- Prova de que userid/nome vêm do DOMÍNIO (síntese, não blob cravado nem handle de captura) ----

        [Fact]
        public void Synth_Is_Domain_Sourced()
        {
            var a = Hex(LobbyFrames.SessionInfo(RefUserId, RefName));        // 999 + "JP"
            var b = Hex(LobbyFrames.SessionInfo(1234, "ZZ"));               // 0x04d2 + "ZZ"
            Assert.Contains("e7034a50", a);    // userid 999 (e703) + nome "JP" (4a50)
            Assert.Contains("d2045a5a", b);    // userid 1234 (d204) + nome "ZZ" (5a5a)
            Assert.NotEqual(a, b);             // entradas distintas -> frames distintos (não é blob fixo)
            Assert.Contains("646368616e6e656c3031", Hex(LobbyFrames.ChannelList(1234, "ZZ"))); // "dchannel01" é const
        }
    }
}
