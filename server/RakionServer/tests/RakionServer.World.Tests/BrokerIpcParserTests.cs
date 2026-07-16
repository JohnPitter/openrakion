using BrokenServer;
using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BrokerIpcParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("shared-code")]
    public void ParsesCrcProtectedServerInfoWithConfiguredCipher(string code)
    {
        byte[] packet = ServerInfoPacket(code);

        BrokerIpcParseResult parsed = BrokerIpcParser.ReadServerInfo(packet, code);
        BrokerServerInfo info = parsed.Info;

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal((byte)1, info.ServerId);
        Assert.Equal((ushort)2000, info.MaxRooms);
        Assert.Equal((ushort)12, info.UsedRooms);
        Assert.Equal((ushort)500, info.MaxUsers);
        Assert.Equal((ushort)34, info.UsedUsers);
    }

    [Fact]
    public void RejectsTamperingAfterDecode()
    {
        byte[] packet = ServerInfoPacket("shared-code");
        packet[10] ^= 0x40;

        BrokerIpcParseResult parsed = BrokerIpcParser.ReadServerInfo(packet, "shared-code");
        Assert.False(parsed.Success);
        Assert.Equal("invalid_crc", parsed.Error);
    }

    [Fact]
    public void RejectsTruncatedPacket()
    {
        byte[] packet = ServerInfoPacket("")[..15];

        BrokerIpcParseResult parsed = BrokerIpcParser.ReadServerInfo(packet, "");
        Assert.False(parsed.Success);
        Assert.Equal("invalid_packet", parsed.Error);
    }

    private static byte[] ServerInfoPacket(string code)
    {
        using var writer = new PacketWriter();
        writer.WriteWord(257);
        writer.WriteByte(123);
        writer.WriteByte(2);
        writer.WriteWord(9);
        writer.WriteByte(1);
        writer.WriteWord(2000);
        writer.WriteWord(12);
        writer.WriteWord(500);
        writer.WriteWord(34);
        writer.AddCrc();
        byte[] packet = writer.ToArray();
        IpcCodec.Encode(packet, code);
        return packet;
    }
}
