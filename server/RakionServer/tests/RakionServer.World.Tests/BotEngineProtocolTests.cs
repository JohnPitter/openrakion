using System;
using System.Buffers.Binary;
using RakionServer.World.BotEngine;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BotEngineProtocolTests
{
    [Fact]
    public void RequestHeaderMatchesNativeContract()
    {
        byte[] frame = BotEngineFrameCodec.EncodeRequest(
            BotEngineProtocol.MessageType.Ping,
            0x11223344,
            [0xAA, 0xBB]);

        Assert.Equal(22, frame.Length);
        Assert.Equal(0x4842524Fu, BinaryPrimitives.ReadUInt32LittleEndian(frame));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)));
        Assert.Equal(0x11223344u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(16)));
        Assert.Equal(new byte[] { 0xAA, 0xBB }, frame[20..]);
    }

    [Fact]
    public void LoadFieldPayloadMatchesNativeLayout()
    {
        byte[] payload = BotEngineFrameCodec.EncodeLoadField(
            7,
            12,
            209,
            2,
            @"LevelsSV\Cage\Cage.wld");

        Assert.Equal(268, payload.Length);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(12, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4)));
        Assert.Equal(209, payload[6]);
        Assert.Equal(2, payload[7]);
        Assert.Equal(0, payload[^1]);
    }

    [Fact]
    public void AddBotPayloadMatchesNativeLayout()
    {
        byte[] payload = BotEngineFrameCodec.EncodeAddBot(
            new BotEngineBotRequest(17, "BotProbe", "Archer"));

        Assert.Equal(52, payload.Length);
        Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal("BotProbe", ReadAscii(payload.AsSpan(4, 32)));
        Assert.Equal("Archer", ReadAscii(payload.AsSpan(36, 16)));
    }

    [Theory]
    [InlineData(200, @"LevelsSV\Icefield\Icefield.wld")]
    [InlineData(209, @"LevelsSV\Cage\Cage.wld")]
    [InlineData(211, @"LevelsSV\Mammoth\Mammoth.wld")]
    [InlineData(213, @"LevelsSV\EightArenas\EightArenas.wld")]
    public void BattleMapResolvesWireIndex(byte mapId, string expected)
    {
        Assert.Equal(expected, BattleWorldCatalog.Resolve(mapId));
    }

    [Theory]
    [InlineData(199)]
    [InlineData(214)]
    public void BattleMapRejectsUnknownIndex(byte mapId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BattleWorldCatalog.Resolve(mapId));
    }

    [Theory]
    [InlineData(@"..\Data\invalid.wld")]
    [InlineData(@"C:\Rakion\LevelsSV\Mammoth\Mammoth.wld")]
    [InlineData(@"LevelsSV\Mammöth\Mammoth.wld")]
    public void LoadFieldRejectsUnsafeWorldName(string worldName)
    {
        Assert.Throws<ArgumentException>(
            () => BotEngineFrameCodec.EncodeLoadField(
                1, 8, 211, 2, worldName));
    }

    private static string ReadAscii(ReadOnlySpan<byte> value)
    {
        int length = value.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(
            length < 0 ? value : value[..length]);
    }
}
