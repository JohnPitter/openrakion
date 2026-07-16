using System;
using System.Buffers.Binary;
using RakionServer.Common;

namespace BrokenServer;

public readonly record struct BrokerServerInfo(
    byte ServerId, ushort MaxRooms, ushort UsedRooms,
    ushort MaxUsers, ushort UsedUsers);
public readonly record struct BrokerIpcParseResult(
    bool Success, BrokerServerInfo Info, string Error);

public static class BrokerIpcParser
{
    private const ushort ServerInfoOpcode = 257;
    private const byte ServerInfoCommand = 2;
    private const ushort ServerInfoPayloadSize = 9;
    private const int ServerInfoPacketSize = 16;

    public static BrokerIpcParseResult ReadServerInfo(byte[] wireData, string code)
    {
        if (wireData.Length != ServerInfoPacketSize)
            return Failure("invalid_packet");

        byte[] data = (byte[])wireData.Clone();
        IpcCodec.Decode(data, code);
        if (!IpcCodec.VerifyCrc(data))
            return Failure("invalid_crc");
        if (BinaryPrimitives.ReadUInt16LittleEndian(data) != ServerInfoOpcode ||
            data[3] != ServerInfoCommand ||
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)) != ServerInfoPayloadSize)
            return Failure("invalid_contract");

        var info = new BrokerServerInfo(
            data[6],
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(7)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(9)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(11)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(13)));
        return new BrokerIpcParseResult(true, info, "");
    }

    private static BrokerIpcParseResult Failure(string error) =>
        new(false, default, error);
}
