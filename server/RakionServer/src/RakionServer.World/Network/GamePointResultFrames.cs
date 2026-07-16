using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network;

public readonly record struct GamePointCellState(uint ItemHandle, byte Level, uint Exp);

public sealed record GamePointResultSnapshot(
    int FieldHandle, uint AwardedGold, int FieldSecondary,
    CharacterProgressionState Progression, uint Win, uint Lose, uint Draw,
    IReadOnlyList<GamePointCellState> Cells);

public static class GamePointResultFrames
{
    public const ushort MessageType = 0x0a;
    public const int BodyLength = 60;

    public static byte[] Build(GamePointResultSnapshot snapshot)
    {
        if (snapshot.Cells.Count != 3)
            throw new ArgumentException("O resultado exige exatamente três cell slots.", nameof(snapshot));

        using var writer = new PacketWriter();
        writer.WriteInt32(snapshot.FieldHandle);
        writer.WriteUInt32(snapshot.AwardedGold);
        writer.WriteInt32(snapshot.FieldSecondary);
        writer.WriteByte(snapshot.Progression.Level);
        writer.WriteUInt32((uint)Math.Clamp(snapshot.Progression.Exp, 0, uint.MaxValue));
        writer.WriteUInt32(snapshot.Win);
        writer.WriteUInt32(snapshot.Lose);
        writer.WriteUInt32(snapshot.Draw);
        writer.WriteWord(snapshot.Progression.LevelPoint);
        foreach (GamePointCellState cell in snapshot.Cells) writer.WriteUInt32(cell.ItemHandle);
        foreach (GamePointCellState cell in snapshot.Cells) writer.WriteByte(cell.Level);
        foreach (GamePointCellState cell in snapshot.Cells) writer.WriteUInt32(cell.Exp);
        writer.WriteByte(0).WriteByte(0);
        byte[] body = writer.ToArray();
        if (body.Length != BodyLength) throw new InvalidOperationException("Layout 0x0A inválido.");
        return body;
    }
}
