using System;
using System.Buffers.Binary;
using System.Net;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static class NetworkEndpointCodec
    {
        public const int WireSize = 6;
        public static readonly IPEndPoint Unspecified = new(IPAddress.Any, 0);

        public static bool TryRead(ReadOnlySpan<byte> wire, out IPEndPoint endpoint)
        {
            endpoint = default!;
            if (wire.Length < WireSize) return false;

            var address = new IPAddress(wire[..4]);
            int port = BinaryPrimitives.ReadUInt16BigEndian(wire[4..]);
            endpoint = new IPEndPoint(address, port);
            return true;
        }

        public static PacketWriter Write(PacketWriter writer, IPEndPoint endpoint)
        {
            writer.WriteBytes(endpoint.Address.MapToIPv4().GetAddressBytes());
            WritePort(writer, endpoint.Port);
            return writer;
        }

        public static PacketWriter WritePort(PacketWriter writer, int port) =>
            writer.WriteByte((byte)(port >> 8)).WriteByte((byte)port);
    }
}
