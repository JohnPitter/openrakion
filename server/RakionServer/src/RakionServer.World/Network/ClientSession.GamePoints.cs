using System;
using System.Collections.Generic;
using RakionServer.World.Database;
using RakionServer.World.Domain;

namespace RakionServer.World.Network;

public sealed partial class ClientSession
{
    public IReadOnlyList<GamePointCellState> GetGamePointCellStates()
    {
        IReadOnlyList<EquippedCellState> equipped = GetEquippedCellStates();
        var result = new GamePointCellState[3];
        for (int index = 0; index < result.Length; index++)
        {
            EquippedCellState cell = equipped[index];
            result[index] = new GamePointCellState(
                cell.ItemHandle, cell.Level, (uint)Math.Clamp(cell.Exp, 0, uint.MaxValue));
        }
        return result;
    }

    public IReadOnlyList<EquippedCellState> GetEquippedCellStates()
    {
        var result = new EquippedCellState[3];
        for (int index = 0; index < result.Length; index++)
        {
            byte slot = (byte)(index + 10);
            UserItem? item = FindActiveItem(_potionRowId[slot]);
            result[index] = item == null
                ? new EquippedCellState(slot, 0, 0, 0, 0, 0)
                : new EquippedCellState(slot, item.Id, item.ItemId,
                    checked((uint)Math.Max(0, item.ItemSn)), item.Level, item.Exp);
        }
        return result;
    }

    public void ApplyCellProgression(IReadOnlyList<CellProgressionChange> changes)
    {
        foreach (CellProgressionChange change in changes)
        {
            if (change.After.RowId <= 0) continue;
            UserItem? item = FindActiveItem(change.After.RowId);
            if (item == null) continue;
            item.Level = change.After.Level;
            item.Exp = change.After.Exp;
            _potionLevel[change.After.Slot] = change.After.Level;
        }
    }

    private UserItem? FindActiveItem(int rowId)
    {
        if (rowId <= 0) return null;
        foreach (UserItem item in Items)
            if (item.Id == rowId) return item;
        return null;
    }
}
