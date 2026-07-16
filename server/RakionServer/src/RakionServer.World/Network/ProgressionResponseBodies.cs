using System;
using System.Collections.Generic;
using RakionServer.Common;

namespace RakionServer.World.Network;

public static class ProgressionResponseBodies
{
    public const int LevelUpLength = 3;
    public const int FieldLevelsLength = 5;

    public static byte[] LevelUp(byte level, ushort levelPoints)
    {
        using var writer = new PacketWriter();
        writer.WriteByte(level).WriteWord(levelPoints);
        return writer.ToArray();
    }

    public static byte[] FieldLevels(
        byte seat, byte playerLevel, IReadOnlyList<byte> cellLevels)
    {
        if (cellLevels.Count != 3)
            throw new ArgumentException("O snapshot exige três níveis de cell.", nameof(cellLevels));

        using var writer = new PacketWriter();
        writer.WriteByte(seat).WriteByte(playerLevel);
        foreach (byte level in cellLevels) writer.WriteByte(level);
        return writer.ToArray();
    }
}
