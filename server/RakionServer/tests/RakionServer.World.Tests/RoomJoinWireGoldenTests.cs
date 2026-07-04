using System.Net;
using RakionServer.World;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test do WIRE do join de sala contra a captura BYTE-A-BYTE do servidor ORIGINAL (orig_capture2),
    /// para validar o formato SEM depender de teste in-game manual. A captura é de um join de 2 clientes (master
    /// "JP2" uid6 slot0, joiner "JP" uid7 slot10, sala "asdasdasd", 216B). ACHADO (2026-07-04) desta suíte:
    /// <list type="bullet">
    /// <item>0x38 member-join = FORMATO CORRETO (bate byte-a-byte, só o endereço P2P difere por NAT do Docker).</item>
    /// <item>0x37 roster: o RECORD é o mesmo do 0x38 (ok); o que DIVERGE é (a) o header (maxPlayers/map do quarto —
    /// a captura é de OUTRA sala) e (b) slots LOCKED (`05`): o original tranca slots 6-9 e 15-19 (além do
    /// maxPlayers), eu emito todos abertos. A trava in-game do 2º cliente NÃO é o record (funciona no 0x38).</item>
    /// </list>
    /// </summary>
    public class RoomJoinWireGoldenTests
    {
        private static string Hex(byte[] b) => System.Convert.ToHexString(b).ToLowerInvariant();

        // S>C 0x38 broadcast do joiner (JP, seat 0x0a=10, uid 7), capturado do original (96B).
        private const string CapturedMemberJoin =
            "3800000a010700004a50000000ac110001d6317f00000108fd0001000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001100010101010000000000";

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
        public void RoomStateHeader_MatchesOriginalCapture()
        {
            // O header de 16B do 0x37 bate byte-a-byte com a captura p/ o mesmo cenário (master@+2, id@+6, etc.).
            var f = new Field(0x03d2)
            {
                State = 1, MasterSlot = 0, Mode = 1, MaxPlayers = 0x0a, MapId = 0,
                MinLevel = 1, MaxLevel = 0x0b, MapSlot = 0x012c, FragLimit = 0x14, Name = "asdasdasd",
            };
            byte[] frame = WorldServer.BuildRoomState(f);
            Assert.Equal("370000000100d203010a00010b2c0114", Hex(frame[..16]));
        }
    }
}
