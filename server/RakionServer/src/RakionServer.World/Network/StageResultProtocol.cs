using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network;

public enum StageResultParseError : byte
{
    None,
    HeaderTruncated,
    StageOutOfRange,
    RankOutOfRange,
    SlotCountOutOfRange,
    LengthMismatch,
}

public static class StageResultProtocol
{
    private const int FixedTailLength = 20;

    public static bool TryParse(
        ReadOnlySpan<byte> data, out StageResultReport? report,
        out StageResultParseError error)
    {
        report = null;
        if (data.Length < 3) return Fail(StageResultParseError.HeaderTruncated, out error);

        byte stage = data[0];
        byte rank = data[1];
        byte count = data[2];
        if (stage >= 100) return Fail(StageResultParseError.StageOutOfRange, out error);
        if (rank >= 6) return Fail(StageResultParseError.RankOutOfRange, out error);
        if (count >= 5) return Fail(StageResultParseError.SlotCountOutOfRange, out error);

        int expectedLength = 3 + count * 2 + FixedTailLength;
        if (data.Length != expectedLength)
            return Fail(StageResultParseError.LengthMismatch, out error);

        var slots = new ushort[count];
        int offset = 3;
        for (int index = 0; index < count; index++, offset += 2)
            slots[index] = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        uint exp = ReadUInt32(data, ref offset);
        uint gold = ReadUInt32(data, ref offset);
        uint cell0 = ReadUInt32(data, ref offset);
        uint cell1 = ReadUInt32(data, ref offset);
        uint cell2 = ReadUInt32(data, ref offset);
        report = new StageResultReport(stage, rank, slots, exp, gold, cell0, cell1, cell2);
        error = StageResultParseError.None;
        return true;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static bool Fail(StageResultParseError value, out StageResultParseError error)
    {
        error = value;
        return false;
    }
}
