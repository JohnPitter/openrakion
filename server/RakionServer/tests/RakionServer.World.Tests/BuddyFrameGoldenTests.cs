using System;
using System.Collections.Generic;
using System.Text;
using RakionServer.Buddy;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test dos frames do messenger (Buddy2.dll) sintetizados por <see cref="BuddyFrames"/> e da resposta
    /// do nick change (<see cref="LobbyFrames.ChangeBuddyNameResult"/>). Os layouts foram cravados no disasm do
    /// OnMsg (FUN_10007420) e do worldserv (FUN_004137a0): registro de amigo de 148B (id ASCII@0, nome UTF-16@0x14,
    /// grupo@0x3c, P2P@0x64=0 offline); NTF_USER_STATE [u16 count] + [id 0x14][online](+[ip:port]×2 network-order);
    /// nick change [u16 0x15][u16 0x0B][status][nick\0] (tamanho strlen+6).
    /// </summary>
    public class BuddyFrameGoldenTests
    {
        private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

        // ---- RET_LOGIN (lista de amigos) -----------------------------------------------------------

        [Fact]
        public void LoginList_Empty_PadsAbove7Bytes() =>      // [result=0][count=0] + pad 6 = 10B (>7 p/ sucesso)
            Assert.Equal("00000000000000000000", Hex(BuddyFrames.LoginList(Array.Empty<BuddyEntry>())));

        [Fact]
        public void LoginList_OneOfflineBuddy_Record0x94()
        {
            // Layout CRAVADO: [result u16][count u16][registro 0x94] — SEM campo token (o cliente lê count@+2;
            // um token @+2 era lido como count e desalinhava tudo -> lista-lixo + crash).
            var buddies = new List<BuddyEntry> { new("acc2", "Heroi2", "") };
            byte[] f = BuddyFrames.LoginList(buddies);

            Assert.Equal(4 + 0x94, f.Length);                         // header (4) + 1 registro de 148B
            Assert.Equal(0x0000, BitConverter.ToUInt16(f, 0));        // result = 0 (sucesso) @+0
            Assert.Equal(1, BitConverter.ToUInt16(f, 2));             // count @+2 (o cliente lê a contagem AQUI)

            int rec = 4;
            Assert.Equal("Heroi2", Encoding.ASCII.GetString(f, rec, 6));         // id ASCII @0x00
            Assert.Equal(0, f[rec + 6]);                                          // null-terminado
            Assert.Equal("Heroi2", Encoding.Unicode.GetString(f, rec + 0x14, 12)); // nome UTF-16 @0x14
            for (int i = 0; i < 8; i++) Assert.Equal(0, f[rec + 0x64 + i]);       // endereço P2P @0x64 = 0 (offline)
        }

        [Fact]
        public void LoginList_TwoBuddies_LengthAndCount()
        {
            var buddies = new List<BuddyEntry> { new("a", "Alpha", ""), new("b", "Beta", "g1") };
            byte[] f = BuddyFrames.LoginList(buddies);
            Assert.Equal(4 + 2 * 0x94, f.Length);                     // header (4) + 2 registros
            Assert.Equal(2, BitConverter.ToUInt16(f, 2));             // count @+2
        }

        // ---- NTF_USER_STATE (presença) -------------------------------------------------------------

        [Fact]
        public void UserState_Online_TwoEqualPairs_NetworkOrder()
        {
            var pres = new UserPresence("Heroi2", true, new byte[] { 127, 0, 0, 1 }, 8500);
            byte[] f = BuddyFrames.UserState(new[] { pres });

            Assert.Equal(2 + 0x14 + 1 + 12, f.Length);               // entry online = 0x21 + [u16 count]
            Assert.Equal(1, BitConverter.ToUInt16(f, 0));            // count
            Assert.Equal("Heroi2", Encoding.ASCII.GetString(f, 2, 6));
            Assert.Equal(1, f[2 + 0x14]);                            // online = 1

            int a = 2 + 0x14 + 1;
            Assert.Equal(new byte[] { 127, 0, 0, 1 }, f[a..(a + 4)]);          // ip1 (network-order)
            Assert.Equal(0x21, f[a + 4]); Assert.Equal(0x34, f[a + 5]);        // port1 = 8500 big-endian (0x2134)
            Assert.Equal(new byte[] { 127, 0, 0, 1 }, f[(a + 6)..(a + 10)]);   // ip2 == ip1 (ativa o P2P)
            Assert.Equal(0x21, f[a + 10]); Assert.Equal(0x34, f[a + 11]);      // port2 == port1
        }

        [Fact]
        public void UserState_Offline_NoAddressBlock()
        {
            byte[] f = BuddyFrames.UserState(new[] { new UserPresence("Heroi2", false, null, 0) });
            Assert.Equal(2 + 0x14 + 1, f.Length);                    // entry offline = 0x15 + [u16 count]
            Assert.Equal(0, f[2 + 0x14]);                            // online = 0
        }

        // ---- nick change (0x15, worldserv FUN_004137a0) --------------------------------------------

        [Fact]
        public void ChangeBuddyNameResult_MatchesWorldservLayout() =>
            // [u16 0x15][u16 0x0B][status=0]["GoHeroi"\0] — tamanho strlen+6 = 13
            Assert.Equal("15000b0000476f486572 6f6900".Replace(" ", ""),
                Hex(LobbyFrames.ChangeBuddyNameResult(0, "GoHeroi")));

        [Fact]
        public void ChangeBuddyNameResult_LengthIsStrlenPlus6() =>
            Assert.Equal("GoHeroi".Length + 6, LobbyFrames.ChangeBuddyNameResult(0, "GoHeroi").Length);
    }
}
