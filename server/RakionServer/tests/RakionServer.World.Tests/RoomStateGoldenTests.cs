using RakionServer.World;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test do header de 16B do 0x37 room-state (<see cref="WorldServer.BuildRoomState"/>), cravado
    /// byte-a-byte da captura do original (orig_capture2, S>C 0x37 ao joiner): o MASTER slot vem em +2 (NÃO o
    /// fieldId, que fica em +6). Regressão nesse layout faz o joiner ler o fieldId como slot do master e se
    /// auto-designar master (mostra START + card fantasma). Ver memória room-state-0x37-master-offset.
    /// </summary>
    public class RoomStateGoldenTests
    {
        private static string Hex(byte[] b) => System.Convert.ToHexString(b).ToLowerInvariant();

        [Fact]
        public void RoomStateHeader_MatchesOriginalCapture()
        {
            // cenário da captura: field 978 (0x03d2), Golem (mode 1), max 10, map 0, level 1~11,
            // mapSlot 300 (0x012c), fragLimit 20 (0x14), state 1 (pré-partida), master no slot 0.
            var f = new Field(0x03d2)
            {
                Mode = 1, MaxPlayers = 10, MapId = 0, MinLevel = 1, MaxLevel = 0x0b,
                MapSlot = 0x012c, FragLimit = 0x14, State = 1, MasterSlot = 0,
            };

            byte[] frame = WorldServer.BuildRoomState(f);

            // header = 16B; o roster (slots vazios) vem depois e não afeta este assert
            Assert.Equal("370000000100d203010a00010b2c0114", Hex(frame[..16]));
        }

        [Fact]
        public void RoomStateHeader_MasterSlotAtOffset2_IsDomainSourced()
        {
            // master no slot 12 (BLUE) -> +2 = 0x0c; prova que o campo vem do domínio, não é constante da captura
            var f = new Field(1) { Mode = 1, MaxPlayers = 8, State = 1, MasterSlot = 12 };
            byte[] frame = WorldServer.BuildRoomState(f);
            Assert.Equal(0x0c, frame[2]);   // MASTER slot em +2
            Assert.Equal(0x01, frame[4]);   // state em +4
            Assert.Equal(0x01, frame[6]);   // fieldId (=1) em +6, LE
        }

        [Fact]
        public void RosterRecord_Is78Bytes_TenShorterThanMemberJoin()
        {
            // Cravado da captura: o record do ROSTER (0x37) e 10B mais curto que o do member-join (0x38).
            // Nome "JP" (2 chars): roster=78B, member-join=88B. A forma de 88B no roster desalinha os slots
            // seguintes (card fantasma). Ver memória room-state-0x37-master-offset.
            byte[] roster = WorldServer.BuildPlayerRecord("JP", 0, 1, rosterForm: true);
            byte[] memberJoin = WorldServer.BuildPlayerRecord("JP", 0, 1, rosterForm: false);
            Assert.Equal(78, roster.Length);
            Assert.Equal(88, memberJoin.Length);
            Assert.Equal(memberJoin.Length - 10, roster.Length);
        }
    }
}
