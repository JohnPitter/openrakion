using System;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Trava byte-a-byte o corpo do CNetMessage 0x30a (move+ação) decodificado da engine.dll
    /// (CPlayerSource::SendAction_Relay @0x103cb0 ↔ CSessionState::GetActionFromMessage @0x10afe0):
    /// 19 bytes, posições packed por SCALE=0.01 (coord×100), heading cru. É a fonte da SÍNTESE do
    /// movimento do bot (nunca relay). Guarda de regressão p/ quando o wrapper UDP for ligado.
    /// </summary>
    public class BotMovementTests
    {
        [Fact]
        public void EncodeActionBody_FixesDecodedLayout()
        {
            var bot = new BotPlayer(1, "Rok", level: 5, charClass: 1, team: 1)
            { X = 1.5f, Y = 2.0f, Z = -3.0f, Yaw = 90f, AimX = 0f, AimY = 0f, AimZ = 0f };

            byte[] b = BotMovement.EncodeActionBody(bot, seat: 11, actState: 0);

            Assert.Equal(19, b.Length);
            Assert.Equal(0, BitConverter.ToUInt16(b, 0));        // [u16 dt]
            Assert.Equal(11, b[2]);                              // [u8 (actState<<5)|slot] = 11
            Assert.Equal(0, b[3]);                               // [u8 reservado]
            Assert.Equal((short)150, BitConverter.ToInt16(b, 4));   // x = 1.5 / 0.01
            Assert.Equal((short)200, BitConverter.ToInt16(b, 6));   // y = 2.0 / 0.01
            Assert.Equal((short)-300, BitConverter.ToInt16(b, 8));  // z = -3.0 / 0.01
            Assert.Equal((short)90, BitConverter.ToInt16(b, 10));   // heading cru
            Assert.Equal(0, b[12]);                              // [u8 flag]
            Assert.Equal((short)0, BitConverter.ToInt16(b, 13));    // aim x
            Assert.Equal((short)0, BitConverter.ToInt16(b, 15));    // aim y
            Assert.Equal((short)0, BitConverter.ToInt16(b, 17));    // aim z
        }

        [Fact]
        public void EncodeActionBody_PacksActStateInHighBits()
        {
            var bot = new BotPlayer(2, "Ares", 5, 1, team: 0);
            byte[] b = BotMovement.EncodeActionBody(bot, seat: 3, actState: 2);
            Assert.Equal((2 << 5) | 3, b[2]);   // 3 bits altos = actState, 5 baixos = slot
        }

        [Fact]
        public void ActionDatagram_IsGated_UntilUdpFramingKnown()
        {
            var bot = new BotPlayer(3, "Vyl", 5, 1, team: 1);
            // Enquanto o wrapper UDP (FUN_36100ef0) não foi confirmado, não emite pacote (no chute).
            Assert.False(BotMovement.UdpFramingKnown);
            Assert.Null(BotMovement.TryBuildActionDatagram(bot, seat: 11));
        }
    }
}
