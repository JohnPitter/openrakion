using System.Collections.Generic;
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

        [Theory]
        [InlineData("JP")]           // 2 chars (o da captura)
        [InlineData("Heroi2")]       // 6 chars (o do teste in-game que gerou o fantasma)
        [InlineData("NomeBemLongo")] // 12 chars
        [InlineData("")]             // vazio
        public void PlayerRecord_IsFixedSize88_RegardlessOfNameLength(string name)
        {
            // O record é FIXO 88B, o MESMO no roster (0x37) e no member-join (0x38). Record VARIÁVEL (crescia com o
            // nome) desalinhava (card fantasma); encurtar p/ 78B CRASHAVA o cliente in-game. Passo constante 88B.
            Assert.Equal(88, WorldServer.BuildPlayerRecord(name, 0, 1).Length);
        }

        [Fact]
        public void PlayerRecord_KeepsEquipTailAtEnd_ForObserverFix()
        {
            // O bloco de equip default (observer-fix) deve ficar nos ÚLTIMOS 11B, offset fixo, p/ qualquer nome.
            byte[] rec = WorldServer.BuildPlayerRecord("Heroi2", 0, 1);
            byte[] tail = { 0x11, 0x00, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Assert.Equal(tail, rec[^11..]);
        }

        /// <summary>Faz o parse do roster do 0x37 EXATAMENTE como o cliente: header 16B + 3 C-strings + 20 slots
        /// (vazio/locked = 1B; ocupado = [state][uid u16][team][record 88B FIXO]). Devolve (slotIndex, name) dos
        /// ocupados. Se o record NÃO for de tamanho fixo, o passo desalinha e um slot vazio vira "ocupado" = fantasma.</summary>
        private static List<(int slot, string name)> ParseRosterOccupied(byte[] frame)
        {
            int p = 16;                                   // header
            for (int c = 0; c < 3; c++) { while (frame[p] != 0) p++; p++; }   // name\0 pw\0 desc\0
            var occ = new List<(int, string)>();
            for (int slot = 0; slot < 20; slot++)
            {
                byte st = frame[p];
                if (st == 1)
                {
                    int recStart = p + 4;                 // [state][uid u16][team] = 4B
                    int nameEnd = recStart; while (frame[nameEnd] != 0) nameEnd++;
                    occ.Add((slot, System.Text.Encoding.ASCII.GetString(frame, recStart, nameEnd - recStart)));
                    p = recStart + 88;                    // passo FIXO de 88B por record
                }
                else p += 1;                              // vazio (0) ou locked (5)
            }
            return occ;
        }

        [Fact]
        public void RoomStateRoster_AlignsWithLongNames_NoPhantom()
        {
            // Reproduz o cenário do teste in-game que gerou o fantasma: master "Heroi2" (6 chars) no RED slot 0,
            // um rival no BLUE slot 10. Se o record for de tamanho variável, o nome de 6 chars desalinha e o parse
            // acha um slot ocupado a mais (fantasma). Com o record FIXO, alinha: exatamente slot 0 e 10.
            var f = new Field(1) { Mode = 1, MaxPlayers = 10, State = 1, MasterSlot = 0 };
            f.AddBot("Heroi2", 5, 1, team: 0);            // RED  -> slot 0
            f.AddBot("Rival", 3, 1, team: 1);             // BLUE -> slot 10

            var occ = ParseRosterOccupied(WorldServer.BuildRoomState(f));

            Assert.Equal(2, occ.Count);                   // SEM fantasma (nem um 3º slot "ocupado")
            Assert.Equal((0, "Heroi2"), occ[0]);          // master no RED slot 0
            Assert.Equal((10, "Rival"), occ[1]);          // rival no BLUE slot 10
        }
    }
}
