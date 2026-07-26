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
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)));
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
            @"LevelsSV\Cage\Cage.wld");

        Assert.Equal(268, payload.Length);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(12, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6)));
        Assert.Equal(0, payload[^1]);
    }

    [Theory]
    [InlineData(0, @"LevelsSV\Icefield\Icefield.wld")]
    [InlineData(9, @"LevelsSV\Cage\Cage.wld")]
    [InlineData(11, @"LevelsSV\Mammoth\Mammoth.wld")]
    [InlineData(13, @"LevelsSV\EightArenas\EightArenas.wld")]
    public void BattleMapResolvesWireIndex(byte mapId, string expected)
    {
        Assert.Equal(expected, BattleWorldCatalog.Resolve(mapId));
    }

    [Fact]
    public void BattleMapRejectsUnknownIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BattleWorldCatalog.Resolve(14));
    }

    [Theory]
    [InlineData(@"..\Data\invalid.wld")]
    [InlineData(@"C:\Rakion\LevelsSV\Mammoth\Mammoth.wld")]
    [InlineData(@"LevelsSV\Mammöth\Mammoth.wld")]
    public void LoadFieldRejectsUnsafeWorldName(string worldName)
    {
        Assert.Throws<ArgumentException>(
            () => BotEngineFrameCodec.EncodeLoadField(1, 8, worldName));
    }
}
