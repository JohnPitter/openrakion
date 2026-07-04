using System.Net;
using RakionServer.World;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test do WIRE do join de sala contra a captura BYTE-A-BYTE do servidor ORIGINAL (orig_capture2),
    /// p/ validar o formato SEM depender de teste in-game manual. Cenário da captura: sala "asdasdasd" na
    /// Gravity (map 0xd2=210), mode 3, Lv 1~10, max 12, 11 rounds, dur/slot 300, frag 0x14; master "JP2"
    /// (uid 6, slot 0), joiner "JP" (uid 7, slot 10). ACHADOS (2026-07-04) desta suíte:
    /// <list type="bullet">
    /// <item>0x38 member-join: record VARIÁVEL COM cauda de 11B (88B p/ "JP") — bate byte-a-byte.</item>
    /// <item>0x37 room-state: +6/+7 do header = MAP/MODE (não fieldId!); record SEM a cauda (77B p/ "JP");
    /// slots além de MaxPlayers/2 por time TRANCADOS (05); 3 slots observer (05) + u32 0 no fim.</item>
    /// </list>
    /// </summary>
    public class RoomJoinWireGoldenTests
    {
        private static string Hex(byte[] b) => System.Convert.ToHexString(b).ToLowerInvariant();

        // S>C 0x38 broadcast do joiner (JP, seat 0x0a=10, uid 7), capturado do original (96B).
        private const string CapturedMemberJoin =
            "3800000a010700004a50000000ac110001d6317f00000108fd0001000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001100010101010000000000";

        // S>C 0x37 room-state COMPLETO ao joiner, capturado do original (216B).
        private const string CapturedRoomState =
            "370000000100d203010a00010b2c0114617364617364617364000000010600004a5032000000ac110001e6937f00000108fe00010000" +
            "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000005050505010700004a50000000ac110001d6317f00000108fd00010000000000000000000000000000000000000000" +
            "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000505050505050500000000";

        /// <summary>Field espelhando a sala da captura (params do C>S 0x3b: d2 03 0b 2c01 14 01 0a).</summary>
        private static Field CaptureField() => new Field(0)
        {
            State = 1, MasterSlot = 0, MapId = 0xd2, Mode = 3, MinLevel = 1, MaxLevel = 0x0a,
            MaxRounds = 0x0b, MapSlot = 0x012c, FragLimit = 0x14, MaxPlayers = 12, Name = "asdasdasd",
        };

        [Fact]
        public void MemberJoin_MatchesOriginalCapture_ExceptNatPort()
        {
            // pair1 = 172.17.0.1:0xd631 (externo); pair2 (loopback) = 0x08fd (2301, porta INTERNA). As duas portas
            // diferem por causa do NAT do Docker; no localhost do usuário seriam iguais (formato correto). Mascaramos
            // o endereço P2P (12B) e validamos que TODO o resto bate: opcode/seat/uid/record (nome/class/level/cauda).
            var ep = new IPEndPoint(IPAddress.Parse("172.17.0.1"), 0xd631);
            byte[] mine = WorldServer.BuildMemberJoin("JP", charClass: 0, level: 1, uid: 7, seat: 10, peerEp: ep);
            byte[] cap = System.Convert.FromHexString(CapturedMemberJoin);
            const int addrOff = 8 + 3 + 1 + 1, addrLen = 12;   // header 8B + record[nome"JP\0"(3)+tag(1)+slot(1)]
            for (int i = addrOff; i < addrOff + addrLen; i++) { mine[i] = 0; cap[i] = 0; }
            Assert.Equal(Hex(cap), Hex(mine));
        }

        [Fact]
        public void RoomState_FullFrame_MatchesOriginalCapture_ExceptUidAndAddr()
        {
            // Ocupantes como BOTS (mesmo serializador); uid sintético e addr zerado são MASCARADOS — o resto
            // (header, strings, slot-states/locked, records SEM cauda, observers, trailer) tem de bater 100%.
            var f = CaptureField();
            Seat(f, 0, "JP2");
            Seat(f, 10, "JP");
            byte[] mine = WorldServer.BuildRoomState(f);
            byte[] cap = System.Convert.FromHexString(CapturedRoomState);
            Assert.Equal(cap.Length, mine.Length);   // 216B — record 11B mais longo/curto desloca TUDO
            // offsets: strings acabam em 28; slot0: uid@29-30, addr@38-49 ("JP2\0"=4 +tag+slotInBlob);
            // record 78B -> slots 1-9 @110..118; slot10: uid@120-121, addr@128-139 ("JP\0"=3 +tag+slotInBlob).
            Mask(mine, cap, 29, 2); Mask(mine, cap, 38, 12);
            Mask(mine, cap, 120, 2); Mask(mine, cap, 128, 12);
            Assert.Equal(Hex(cap), Hex(mine));
        }

        [Fact]
        public void RoomState_Header_MapAndModeAtOffset6And7_AreDomainSourced()
        {
            // Regressão do bug da TRAVADA do joiner: escrever o fieldId u16 em +6 fazia o cliente ler
            // map=idLow/mode=idHigh (sala Gravity/PvP virava "map 0 mode 0").
            byte[] frame = WorldServer.BuildRoomState(CaptureField());
            Assert.Equal("370000000100d203010a00010b2c0114", Hex(frame[..16]));
            Assert.Equal(0xd2, frame[6]);   // MAP (210 = Gravity), NÃO fieldId
            Assert.Equal(0x03, frame[7]);   // MODE
        }

        private static void Seat(Field f, int slot, string name)
        {
            f.Slots[slot].Bot = new BotPlayer(slot + 1, name, level: 1, charClass: 0, team: (byte)(slot < 10 ? 0 : 1));
            f.Slots[slot].State = 1;
        }

        private static void Mask(byte[] a, byte[] b, int off, int len)
        {
            for (int i = off; i < off + len; i++) { a[i] = 0; b[i] = 0; }
        }
    }
}
