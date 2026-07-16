using System;

namespace RakionServer.World.Domain;

public readonly record struct EquippedCellState(
    byte Slot, int RowId, int ItemId, uint ItemHandle, byte Level, long Exp);

public readonly record struct CellProgressionChange(
    EquippedCellState Before, EquippedCellState After, uint ReportedExp, uint AppliedExp);

public static class CellProgression
{
    public static CellProgressionChange Project(
        EquippedCellState current, uint reportedExp,
        Func<int, byte, long?> threshold, long level99Cap,
        uint maxAppliedExp = 100)
    {
        if (current.RowId <= 0 || current.ItemId < 8000 || current.ItemId >= 9000 ||
            current.Exp < 0 || reportedExp == 0 || level99Cap <= 0)
            return new CellProgressionChange(current, current, reportedExp, 0);

        uint appliedExp = Math.Min(reportedExp, maxAppliedExp);
        long exp = checked(current.Exp + appliedExp);
        byte level = current.Level;
        int npc = current.ItemId - 8000;

        if (level >= 99)
        {
            exp = Math.Min(exp, level99Cap);
        }
        else
        {
            while (exp != 0 && level < 99)
            {
                long? next = threshold(npc, level);
                if (next is null || next.Value >= exp) break;
                level++;
            }
        }

        EquippedCellState after = current with { Level = level, Exp = exp };
        return new CellProgressionChange(current, after, reportedExp, appliedExp);
    }
}
