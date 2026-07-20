using System.Net;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test dos frames da cadeia lobby->canal->sala->stage SINTETIZADOS por <see cref="LobbyFrames"/>.
    /// Os builders foram cravados da decompilação do worldserv (objdump): cada frame é emitido com seu LEN
    /// REAL (3º arg do send). Os bytes além do LEN real, na captura, eram LIXO DE STACK e variam entre
    /// sessões, portanto não integram os arrays lógicos. As CONSTANTES e o DADO DE SESSÃO (userid 999=0x03e7,
    /// nome "JP", stage 432s) são exigidos
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
        public void Endpoints_DistinctPorts_MatchOriginalCapture() =>
            Assert.Equal("0e00007f000001c9fc7f000001c9fd",
                Hex(LobbyFrames.Endpoints(
                    new IPEndPoint(IPAddress.Loopback, 51708),
                    new IPEndPoint(IPAddress.Loopback, 51709))));

        [Fact]
        public void Endpoints_WithoutAuthenticatedUdp_DoNotPublishLoopbackFallback() =>
            Assert.Equal("0e0000000000000000000000000000",
                Hex(LobbyFrames.Endpoints(
                    NetworkEndpointCodec.Unspecified, NetworkEndpointCodec.Unspecified)));

        // ---- Acks curtos cravados da decompilação: LEN real + zero-pad (cauda capturada era lixo de stack) ----

        [Fact]
        public void CharacterSelectAck_HasExactLogicalLength() =>
            Assert.Equal("140000", Hex(LobbyFrames.CharacterSelectAck()));

        [Theory]
        [InlineData(Database.CharacterSelectStatus.SystemError, "140001")]
        [InlineData(Database.CharacterSelectStatus.NotFound, "140002")]
        public void CharacterSelect_ErrorStatusesAreCanonical(
            Database.CharacterSelectStatus status, string expected) =>
            Assert.Equal(expected, Hex(LobbyFrames.CharacterSelectAck(status)));

        [Theory]
        [InlineData(1, "130001")]
        [InlineData(2, "130002")]
        [InlineData(3, "130003")]
        [InlineData(5, "130005")]
        [InlineData(6, "130006")]
        [InlineData(7, "130007")]
        [InlineData(9, "130009")]
        public void CharacterDeleteAck_IsDeterministic(byte status, string expected) =>
            Assert.Equal(expected, Hex(LobbyFrames.CharacterDeleteAck(status)));

        [Fact]
        public void CharacterDeleteSuccessCarriesOriginalClanSnapshot() =>
            Assert.Equal(
                "1300004433221105436c616e000403020106050a0908070e0d0c0b1211100f4d617374657200",
                Hex(LobbyFrames.CharacterDeleteAck(0, new ClanLoginSnapshot
                {
                    Id = 0x11223344,
                    Grade = 5,
                    Name = "Clan",
                    Rank = 0x01020304,
                    Members = 0x0506,
                    Point = 0x0708090A,
                    MemberPoint = 0x0B0C0D0E,
                    MemberRank = 0x0F101112,
                    MasterCharacterName = "Master",
                })));

        [Fact]
        public void GameListEmpty_RealLen3_ZeroPad() =>      // RE FUN_00422c90/@0x41c0b7: lista vazia (solo) = LEN=3 [36 00][00]
            Assert.Equal("360000000000000000000000", Hex(LobbyFrames.GameListEmpty()));

        [Fact]
        public void RoomCreateAck_RealLen5_ZeroPad() =>      // RE FUN_00423580: LEN=5 [3b 00][status][seat]; resto era lixo
            Assert.Equal("3b0000000000000000000000", Hex(LobbyFrames.RoomCreateAck()));

        [Fact]
        public void SoloStageCreateAck_PreservesRequestSequenceAndOriginalLayout()
        {
            var options = new RoomCreationOptions
            {
                Name = "stage", Password = "pw", Description = "desc", MapId = 2,
                Mode = 0, Rounds = 1, DurationSeconds = 432, FragLimit = 0,
                MinLevel = 1, MaxLevel = 10, LevelRangeCode = 0
            };

            Assert.Equal(
                "050025000073746167650070770064657363000201b00100010a00",
                Hex(LobbyFrames.SoloStageCreateAck(5, 0, options)));
        }

        [Fact]
        public void MatchStartAck_RealLen3_ZeroPad() =>      // RE FUN_004079d0: LEN=3 [43 00][status]; [handle][3b...] era lixo
            Assert.Equal("430000000000000000000000", Hex(LobbyFrames.MatchStartAck()));

        [Theory]
        [InlineData(1, "430001000000000000000000")]
        [InlineData(2, "430002000000000000000000")]
        [InlineData(3, "430003000000000000000000")]
        public void MatchStartAck_PreservesFailureStatus(byte status, string expected) =>
            Assert.Equal(expected, Hex(LobbyFrames.MatchStartAck(status)));

        [Fact]
        public void GameList_SerializesOriginalFieldEntryOrderAndPadsBlock()
        {
            var field = new RoomListSnapshot(
                7, true, false, 3, 2, 5, 40, 9, 1, 3, 2, 12,
                0x11223344, 10, "Sala", 0x5566);

            Assert.Equal(
                "3600010700010003020528090103020c443322110a0000000000000053616c6100665500",
                Hex(LobbyFrames.GameList(new[] { field })));
        }

        [Fact]
        public void RoomJoinAndCreateResults_PreserveIdentifiersAndStatus()
        {
            Assert.Equal("3b0002090000000000000000", Hex(LobbyFrames.RoomCreateAck(9, 2)));
            Assert.Equal("380003000000000000000000", Hex(LobbyFrames.RoomJoinResult(3)));
        }

        [Fact]
        public void StageEndResult_Clear_RealLen6_ZeroPad() =>   // RE FUN_00405a90: 2bd=2 (clear) -> 6 bytes + padding
            Assert.Equal("4a0002010100000000000000", Hex(LobbyFrames.StageEndResult(2)));

        [Fact]
        public void StageEndResult_Death_RealLen6_ZeroPad() =>   // RE GameDiePlayer FUN_004087d0: 2bd=1 (morte) -> mesma forma, byte[2]=1
            Assert.Equal("4a0001010100000000000000", Hex(LobbyFrames.StageEndResult(1)));

        [Fact]
        public void InventoryEnterAck_MatchesOriginalCapture() =>
            Assert.Equal("2c00008deb863f",
                Hex(LobbyFrames.InventoryEnterAck(0x3f86eb8d)));

        [Theory]
        [InlineData(1, "2c000100000000")]
        [InlineData(2, "2c000200000000")]
        public void InventoryEnterResult_UsesStatusAndZeroReference(byte status, string expected) =>
            Assert.Equal(expected, Hex(LobbyFrames.InventoryEnterResult(status)));

        [Theory]
        [InlineData(0, "2d0000")]
        [InlineData(1, "2d0001")]
        [InlineData(2, "2d0002")]
        public void InventoryLeaveResult_UsesLogicalThreeByteFrame(byte status, string expected) =>
            Assert.Equal(expected, Hex(LobbyFrames.InventoryLeaveResult(status)));

        [Theory]
        [InlineData(false, 3, "2e0003")]
        [InlineData(true, 3, "2f0003")]
        public void StorageMutationError_UsesRequestOpcode(
            bool sale, byte status, string expected) =>
            Assert.Equal(expected, Hex(LobbyFrames.StorageMutationError(sale, status)));

        [Fact]
        public void StorageSaleAck_MatchesOriginalMinimalSnapshotLayout() =>
            Assert.Equal(
                "150000004433221188776655e903380400000780841e0005341200000000",
                Hex(LobbyFrames.StorageSaleAck(
                    0x11223344, 0x55667788, 1001, 1080, 7, 2_000_000, 5, 0x1234)));

        [Fact]
        public void StageResultAck_UsesCapturedRealLength() =>
            Assert.Equal("530000020400", Hex(LobbyFrames.StageResultAck(2, 4, System.Array.Empty<ushort>())));

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
            // 0x64 é owner sentinel 100; o nome real começa em "channel01".
            Assert.Contains("646368616e6e656c3031", Hex(LobbyFrames.ChannelList(1234, "ZZ")));
        }
    }
}
