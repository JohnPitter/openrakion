using System;
using System.Collections.Generic;
using RakionServer.World.Database;
using RakionServer.World.Domain;

namespace RakionServer.World.CharSelect
{
    public sealed record CharacterPreview(ushort[] Equipment, byte[] Enhancement);

    public static class CharacterPreviewProjection
    {
        public static CharacterPreview Build(
            CharacterInfo character, IReadOnlyList<UserItem> items,
            Func<int, ItemDef?> findDefinition)
        {
            var equipment = new ushort[EquipmentRules.GearSlotCount];
            var enhancement = new byte[EquipmentRules.GearSlotCount];
            foreach (UserItem item in items)
            {
                if (!EquipmentRules.IsGearSlot(item.Slot)) continue;
                ItemDef? definition = findDefinition(item.ItemId);
                if (definition == null ||
                    !EquipmentRules.CanPlace(
                        definition, item.Slot, character.Class, character.Level)) continue;
                equipment[item.Slot] = checked((ushort)item.ItemId);
                enhancement[item.Slot] = item.Level;
            }
            return new CharacterPreview(equipment, enhancement);
        }
    }
}
