using System;
using System.Buffers.Binary;
using System.Net;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public class GameplayUdpHandshakeTests
    {
        [Theory]
        [InlineData(GameplayUdpHandshake.Port1Type)]
        [InlineData(GameplayUdpHandshake.Port2Type)]
        public void ParsesOriginalSevenByteEnvelope(ushort type)
        {
            byte[] packet = Build(type, 17, 0x12345678, 0xa1b2c3d4);

            Assert.True(GameplayUdpHandshake.TryParse(packet, type, out var value));
            Assert.Equal((ushort)17, value.Slot);
            Assert.Equal(0x12345678u, value.SessionKey);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 2301), value.AdvertisedEndpoint);
            Assert.Equal(0xa1b2c3d4u, value.EchoData);
        }

        [Fact]
        public void RejectsShortPacketAndWrongPortType()
        {
            byte[] packet = Build(GameplayUdpHandshake.Port1Type, 1, 2, 3);

            Assert.False(GameplayUdpHandshake.TryParse(packet.AsSpan(0, 22), GameplayUdpHandshake.Port1Type, out _));
            Assert.False(GameplayUdpHandshake.TryParse(packet, GameplayUdpHandshake.Port2Type, out _));
        }

        [Fact]
        public void ParsesCapturedPrivatePeerEndpointInNetworkByteOrder()
        {
            byte[] packet = Convert.FromHexString("010201000000000400144300007F00000108FDEB54EA1B");

            Assert.True(GameplayUdpHandshake.TryParse(packet, GameplayUdpHandshake.Port1Type, out var value));
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, 2301), value.AdvertisedEndpoint);
        }

        [Theory]
        [InlineData(0, "0102d4c3b2a10000d4c3b2a1")]
        [InlineData(1, "0102d4c3b2a10101d4c3b2a1")]
        public void BuildsOriginalEcho(byte endpointIndex, string expectedHex)
        {
            byte[] packet = Build(GameplayUdpHandshake.Port1Type, 1, 2, 0xa1b2c3d4);
            GameplayUdpHandshake.TryParse(packet, GameplayUdpHandshake.Port1Type, out var value);

            Assert.Equal(expectedHex, Convert.ToHexString(value.BuildEcho(endpointIndex)).ToLowerInvariant());
        }

        private static byte[] Build(ushort type, ushort slot, uint key, uint echoData)
        {
            byte[] packet = new byte[GameplayUdpHandshake.PacketSize];
            BinaryPrimitives.WriteUInt16LittleEndian(packet, type);
            packet[2] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(7), slot);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(9), key);
            IPAddress.Parse("1.2.3.4").GetAddressBytes().CopyTo(packet, 13);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(17), 2301);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(19), echoData);
            return packet;
        }
    }
}
