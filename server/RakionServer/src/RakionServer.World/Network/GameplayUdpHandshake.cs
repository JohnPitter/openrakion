using System;
using System.Buffers.Binary;
using System.Net;

namespace RakionServer.World.Network
{
    public readonly record struct GameplayUdpHandshake
    {
        public const int PacketSize = 23;
        public const ushort Port1Type = 0x0201;
        public const ushort Port2Type = 0x0202;

        public ushort Type { get; init; }
        public ushort Slot { get; init; }
        public uint SessionKey { get; init; }
        public IPEndPoint AdvertisedEndpoint { get; init; }
        public uint EchoData { get; init; }

        public static bool TryParse(ReadOnlySpan<byte> packet, ushort expectedType, out GameplayUdpHandshake value)
        {
            value = default;
            if (packet.Length < PacketSize) return false;

            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(packet);
            if (type != expectedType) return false;

            if (!NetworkEndpointCodec.TryRead(packet[13..19], out var advertisedEndpoint)) return false;

            value = new GameplayUdpHandshake
            {
                Type = type,
                Slot = BinaryPrimitives.ReadUInt16LittleEndian(packet[7..]),
                SessionKey = BinaryPrimitives.ReadUInt32LittleEndian(packet[9..]),
                AdvertisedEndpoint = advertisedEndpoint,
                EchoData = BinaryPrimitives.ReadUInt32LittleEndian(packet[19..])
            };
            return true;
        }

        public byte[] BuildEcho(byte endpointIndex)
        {
            byte[] echo = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(echo, Port1Type);
            BinaryPrimitives.WriteUInt32LittleEndian(echo.AsSpan(2), EchoData);
            echo[6] = endpointIndex;
            echo[7] = endpointIndex;
            BinaryPrimitives.WriteUInt32LittleEndian(echo.AsSpan(8), EchoData);
            return echo;
        }
    }
}
