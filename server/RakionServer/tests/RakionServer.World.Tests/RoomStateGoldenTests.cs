using RakionServer.World;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test dos CAMPOS do 0x37 room-state (<see cref="WorldServer.BuildRoomState"/>) vindos do domínio
    /// (não constantes da captura). Layout do header cravado da captura (ver RoomJoinWireGoldenTests p/ o frame
    /// completo): +2 = MASTER slot (regressão = joiner se auto-designa master/START), +6 = MAP, +7 = MODE
    /// (regressão = joiner lê "map 0 mode 0" e trava). Ver memória room-state-0x37-master-offset.
    /// </summary>
    public class RoomStateGoldenTests
    {
        [Fact]
        public void RoomStateHeader_FieldsAreDomainSourced()
        {
            // master no slot 12 (BLUE), sala Golem na Gravity — prova que os campos vêm do domínio.
            var f = new Field(1)
            {
                Mode = (byte)GameMode.Golem, MaxPlayers = 8, State = 1, MasterSlot = 12,
                MapId = 210, MinLevel = 1, MaxLevel = 99, MaxRounds = 3,
            };
            byte[] frame = WorldServer.BuildRoomState(f);
            Assert.Equal(0x0c, frame[2]);   // MASTER slot em +2
            Assert.Equal(0x01, frame[4]);   // state em +4
            Assert.Equal(210, frame[6]);    // MAP em +6 (NÃO o fieldId)
            Assert.Equal(0x01, frame[7]);   // MODE em +7 (Golem)
            Assert.Equal(0x01, frame[8]);   // minLevel
            Assert.Equal(99, frame[9]);     // maxLevel
            Assert.Equal(0x03, frame[12]);  // maxRounds
        }

        [Fact]
        public void RoomState_SlotsBeyondCapacity_AreLocked()
        {
            // max 8 (4v4): abertos 0-3/10-13, TRANCADOS (05) 4-9/14-19, + 3 observers 05 + u32 0 no fim.
            var f = new Field(1) { Mode = 1, MaxPlayers = 8, State = 1, MasterSlot = 0 };
            byte[] frame = WorldServer.BuildRoomState(f);
            int o = 16 + 1 + 1 + 1;                       // header + name\0 + pass\0 + desc\0 (vazios)
            byte[] slots = frame[o..(o + 20)];
            Assert.Equal(new byte[] { 0, 0, 0, 0, 5, 5, 5, 5, 5, 5, 0, 0, 0, 0, 5, 5, 5, 5, 5, 5 }, slots);
            Assert.Equal(new byte[] { 5, 5, 5, 0, 0, 0, 0 }, frame[^7..]);   // observers + u32 0
        }

        [Fact]
        public void PlayerRecord_EquipTail_OnlyInMemberJoinForm()
        {
            // A cauda de equip default (11B, observer-fix do stage) existe SÓ no record do 0x38; o roster do
            // 0x37 usa a forma SEM cauda (captura: "JP" = 88B no 0x38 vs 77B no 0x37). Vale p/ qualquer nome.
            byte[] tail = { 0x11, 0x00, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };
            foreach (var name in new[] { "Heroi2", "JP" })
            {
                byte[] with = WorldServer.BuildPlayerRecord(name, 0, 1);
                byte[] without = WorldServer.BuildPlayerRecord(name, 0, 1, equipTail: false);
                Assert.Equal(tail, with[^11..]);
                Assert.Equal(with.Length - 11, without.Length);
                Assert.Equal(with[..^11], without);
            }
        }
    }
}
