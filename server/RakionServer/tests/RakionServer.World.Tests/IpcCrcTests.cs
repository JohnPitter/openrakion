using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Invariante da CRC do IPC broker&lt;-&gt;world (<see cref="IpcCodec.VerifyCrc"/>), de que depende o guard
    /// do BrokerLink p/ separar IPC legítimo do gameplay UDP do cliente que compartilha a porta 40708: como a
    /// BCRC anexada é o XOR dos bytes anteriores, o XOR do pacote INTEIRO é 0 quando íntegro; gameplay/forjado
    /// dá != 0 e é descartado (em vez de virar ServerInfo espúrio / over-read do login).
    /// </summary>
    public class IpcCrcTests
    {
        [Fact]
        public void RequestServerInfo_BuiltWithCrc_Verifies()
        {
            // mesmo formato do broker (PacketRequestServerInfo): [u16 port][u8 rnd][u8 cmd=0][u16 len=0][u8 crc]
            using var p = new PacketWriter();
            p.WriteWord(40708);
            p.WriteByte(123);
            p.WriteByte(0);
            p.WriteWord(0);
            p.AddCrc();
            byte[] pkt = p.ToArray();
            Assert.Equal(7, pkt.Length);            // RequestServerInfo = IPC mínimo (7B)
            Assert.True(IpcCodec.VerifyCrc(pkt));   // XOR total (incl. CRC) == 0
        }

        [Theory]
        [InlineData(new byte[] { 0x0d, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00 })]                          // ACK UDP do cliente (7B) que vazava p/ o IPC
        [InlineData(new byte[] { 0x00, 0x40, 0x6e, 0x01, 0x00, 0x00, 0x00, 0x4d, 0x00, 0x00, 0x00 })]  // input de gameplay (11B), byte[3]=1 (causava o over-read)
        public void GameplayUdp_FailsCrc(byte[] pkt) =>
            Assert.False(IpcCodec.VerifyCrc(pkt));   // sem BCRC -> XOR != 0 -> descartado pelo guard

        [Fact]
        public void TamperedIpc_FailsCrc()
        {
            using var p = new PacketWriter();
            p.WriteWord(40708); p.WriteByte(50); p.WriteByte(1); p.WriteWord(4); p.AddCrc();
            byte[] pkt = p.ToArray();
            pkt[2] ^= 0xff;                          // corrompe 1 byte -> XOR != 0
            Assert.False(IpcCodec.VerifyCrc(pkt));
        }
    }
}
