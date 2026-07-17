using System.Text;
using RakionServer.Buddy;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden tests dos frames do messenger: validam byte-a-byte o layout CRAVADO por RE do Buddy2.dll
    /// (dispatcher CBuddy2::OnMsg). Não há captura do servidor de buddy original (nunca veio no pacote);
    /// a fonte da verdade é o disassembly do loop do RET_LOGIN (@100075d0) e da entry online (@10008340).
    /// </summary>
    public class BuddyFrameGoldenTests
    {
        private static ushort U16(byte[] b, int off) => (ushort)(b[off] | (b[off + 1] << 8));
        private static uint U32(byte[] b, int off) => (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
        private static string Ascii(byte[] b, int off, int max)
        { int n = 0; while (n < max && b[off + n] != 0) n++; return Encoding.ASCII.GetString(b, off, n); }
        private static string Wide(byte[] b, int off, int max)
        { var sb = new StringBuilder(); for (int i = 0; i < max; i++) { ushort c = U16(b, off + i * 2); if (c == 0) break; sb.Append((char)c); } return sb.ToString(); }
        private static void AssertAddr(byte[] b, int off, byte[] ip, ushort port)
        {
            Assert.Equal(ip[0], b[off]); Assert.Equal(ip[1], b[off + 1]); Assert.Equal(ip[2], b[off + 2]); Assert.Equal(ip[3], b[off + 3]);
            Assert.Equal((byte)(port >> 8), b[off + 4]); Assert.Equal((byte)(port & 0xff), b[off + 5]);  // porta network-order
        }

        [Fact]
        public void LoginList_registro_de_amigo_e_token_P2P()
        {
            byte[] f = BuddyFrames.LoginList(new[] { new BuddyEntry("test2", "Heroi2", "") }, 0x11223344u);

            Assert.Equal(8 + 0x94, f.Length);                  // header 8 + 1 registro de 0x94
            Assert.Equal(0, U16(f, 0));                        // result = 0 (sucesso)
            Assert.Equal(0x11223344u, U32(f, 2));              // token P2P @ +2 (o cliente ecoa via UDP)
            Assert.Equal(1, U16(f, 6));                        // count @ +6
            Assert.Equal("Heroi2", Ascii(f, 8 + 0x00, 0x14));  // id ASCII @ reg+0x00
            Assert.Equal("Heroi2", Wide(f, 8 + 0x14, 0x14));   // nome UTF-16 @ reg+0x14 (display)
        }

        [Fact]
        public void UserState_online_carrega_endpoint_P2P()
        {
            byte[] ip = { 127, 0, 0, 1 };
            byte[] on = BuddyFrames.UserState("Heroi2", true, ip, 50807);

            Assert.Equal(2 + 0x21, on.Length);                 // count + entry online (0x21)
            Assert.Equal(1, U16(on, 0));
            Assert.Equal("Heroi2", Ascii(on, 2, 0x14));
            Assert.Equal(1, on[2 + 0x14]);                     // flag online
            AssertAddr(on, 2 + 0x15, ip, 50807);               // par 1 (ip1/port1)
            AssertAddr(on, 2 + 0x1b, ip, 50807);               // par 2 (ip2/port2) == par 1 -> ativa P2P

            byte[] off = BuddyFrames.UserState("Heroi2", false);
            Assert.Equal(2 + 0x15, off.Length);                // entry offline (0x15)
            Assert.Equal(0, off[2 + 0x14]);
        }
    }
}
