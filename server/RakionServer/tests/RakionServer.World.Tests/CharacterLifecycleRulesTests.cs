using System.Net.Sockets;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterLifecycleRulesTests
    {
        [Fact]
        public void NewSessionStartsWithoutSelectedCharacter()
        {
            using var socket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var session = new ClientSession(socket, 1, null!);

            Assert.Equal(0, session.ActiveCharId);
            Assert.Equal(-1, session.PreviewCharId);
        }

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
