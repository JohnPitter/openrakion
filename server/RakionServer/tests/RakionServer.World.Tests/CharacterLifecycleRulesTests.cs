using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterLifecycleRulesTests
    {
        [Theory]
        [InlineData(1, 0, 0, 0, true)]
        [InlineData(0, 0, 0, 0, false)]
        [InlineData(1, 2, 0, 0, false)]
        [InlineData(1, 0, 5, 0, false)]
        [InlineData(1, 0, 0, 6, false)]
        public void CreateRequiresAccountWithoutActiveCharacterAndValidCatalogRange(
            int gameInfoId, int activeCharacterId, byte characterClass, byte slot, bool expected) =>
            Assert.Equal(expected, CharacterLifecycleRules.CanCreate(
                gameInfoId, activeCharacterId, characterClass, slot));

        [Theory]
        [InlineData(1, 0, 10, true)]
        [InlineData(1, 0, -1, true)]
        [InlineData(0, 0, 10, false)]
        [InlineData(1, 2, 10, false)]
        [InlineData(1, 0, 0, false)]
        public void SelectRequiresAccountWithoutActiveCharacterAndPositiveCharacterId(
            int gameInfoId, int activeCharacterId, int characterId, bool expected) =>
            Assert.Equal(expected, CharacterLifecycleRules.CanSelect(
                gameInfoId, activeCharacterId, characterId));
    }
}
