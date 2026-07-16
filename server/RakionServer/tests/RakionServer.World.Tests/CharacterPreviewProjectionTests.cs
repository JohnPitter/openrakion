using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using RakionServer.World.CharSelect;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterPreviewProjectionTests
    {
        [Theory]
        [InlineData(0, 1001)]
        [InlineData(1, 2001)]
        [InlineData(2, 3001)]
        [InlineData(3, 4001)]
        [InlineData(4, 5001)]
        public void FiveClasses_ProjectOnlyCompatibleEquipment(byte characterClass, int firstItemId)
        {
            var character = new CharacterInfo { Class = characterClass, Level = 2 };
            var definitions = BuildDefinitions(characterClass, firstItemId);
            var items = BuildItems(firstItemId);
            items.Add(new UserItem { ItemId = IncompatibleItem(characterClass), Slot = 0, Level = 14 });

            CharacterPreview preview = CharacterPreviewProjection.Build(
                character, items, id => definitions.GetValueOrDefault(id));

            for (int slot = 0; slot < 6; slot++)
            {
                Assert.Equal(firstItemId + slot * 100, preview.Equipment[slot]);
                Assert.Equal(slot + 1, preview.Enhancement[slot]);
            }
            Assert.Equal(6022, preview.Equipment[6]);
            Assert.Equal(7, preview.Enhancement[6]);
        }

        [Fact]
        public void MissingHighLevelAndNonGearItemsRemainOutOfPreview()
        {
            var character = new CharacterInfo { Class = 0, Level = 1 };
            var items = new[]
            {
                new UserItem { ItemId = 1001, Slot = 0, Level = 5 },
                new UserItem { ItemId = 12001, Slot = 6, Level = 1 }
            };
            var definitions = new Dictionary<int, ItemDef>
            {
                [1001] = Definition(1001, 0, 1, requiredLevel: 2),
                [12001] = Definition(12001, 12, 31, requiredLevel: 1)
            };

            CharacterPreview preview = CharacterPreviewProjection.Build(
                character, items, id => definitions.GetValueOrDefault(id));

            Assert.Equal(new ushort[7], preview.Equipment);
            Assert.Equal(new byte[7], preview.Enhancement);
        }

        [Fact]
        public void LoginFrameCarriesPreviewForAllFiveClasses()
        {
            for (byte characterClass = 0; characterClass < 5; characterClass++)
            {
                int firstItemId = (characterClass + 1) * 1000 + 1;
                Dictionary<int, ItemDef> definitions = BuildDefinitions(
                    characterClass, firstItemId);
                CharacterPreview preview = CharacterPreviewProjection.Build(
                    new CharacterInfo { Class = characterClass, Level = 2 },
                    BuildItems(firstItemId),
                    id => definitions.GetValueOrDefault(id));
                var summary = new CharSummary
                {
                    CharacterId = characterClass + 1,
                    Name = "C" + characterClass,
                    Slot = characterClass,
                    Class = characterClass,
                    Level = 2,
                    Equip = preview.Equipment,
                    Enhance = preview.Enhancement
                };
                byte[] frame = LoginCharListWriter.Build(new CharList
                {
                    DisplayName = "JP",
                    Chars = new[] { summary }
                });

                const int fields = 73;
                Assert.Equal((characterClass + 1) * 1000 + 1,
                    BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(fields + 50)));
                Assert.Equal(1, frame[fields + 88]);
            }
        }

        private static Dictionary<int, ItemDef> BuildDefinitions(byte characterClass, int firstItemId)
        {
            var definitions = new Dictionary<int, ItemDef>();
            for (byte slot = 0; slot < 6; slot++)
            {
                int id = firstItemId + slot * 100;
                definitions[id] = Definition(id, slot, 1 << characterClass, requiredLevel: 2);
            }
            definitions[6022] = Definition(6022, 6, 31, requiredLevel: 1);
            definitions[IncompatibleItem(characterClass)] = Definition(
                IncompatibleItem(characterClass), 0, 1 << ((characterClass + 1) % 5), requiredLevel: 1);
            return definitions;
        }

        private static List<UserItem> BuildItems(int firstItemId)
        {
            var items = new List<UserItem>();
            for (byte slot = 0; slot < 6; slot++)
                items.Add(new UserItem { ItemId = firstItemId + slot * 100, Slot = slot, Level = (byte)(slot + 1) });
            items.Add(new UserItem { ItemId = 6022, Slot = 6, Level = 7 });
            return items;
        }

        private static ItemDef Definition(
            int id, byte type, int classMask, byte requiredLevel) => new()
        {
            Id = id,
            Type = type,
            Class = checked((byte)classMask),
            Level = requiredLevel
        };

        private static int IncompatibleItem(byte characterClass) =>
            characterClass == 4 ? 1001 : (characterClass + 2) * 1000 + 1;
    }
}
