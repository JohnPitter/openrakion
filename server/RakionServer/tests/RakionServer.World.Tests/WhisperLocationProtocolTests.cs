using System.Collections.Generic;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class WhisperLocationProtocolTests
    {
        [Fact]
        public void FramesMatchOriginalLayouts()
        {
            Assert.Equal("160000416C696365006F6900",
                System.Convert.ToHexString(WhisperLocationProtocol.Whisper("Alice", "oi")));
            Assert.Equal("160001",
                System.Convert.ToHexString(WhisperLocationProtocol.WhisperNotFound()));
            Assert.Equal("170007012C01",
                System.Convert.ToHexString(WhisperLocationProtocol.WhereAmI(7, new(1, 300))));
            Assert.Equal("180000426F6200000300",
                System.Convert.ToHexString(WhisperLocationProtocol.WhereAreYou("Bob", new(0, 3))));
            Assert.Equal("180001426F6200",
                System.Convert.ToHexString(WhisperLocationProtocol.WhereAreYouNotFound("Bob")));
        }

        [Fact]
        public void LocationUsesChannelForStatusTwoAndFieldForStatusThree()
        {
            CharacterPresenceSnapshot presence = Presence("Alice") with
            {
                Status = UserStatus.FieldLobby,
                ChannelId = 9,
                FieldId = 44
            };

            Assert.True(CharacterPresenceRules.TryGetLocation(presence, out WorldLocation lobby));
            Assert.Equal(new WorldLocation(0, 9), lobby);

            presence = presence with { Status = UserStatus.InField, FieldId = 513 };
            Assert.True(CharacterPresenceRules.TryGetLocation(presence, out WorldLocation field));
            Assert.Equal(new WorldLocation(1, 513), field);
        }

        [Fact]
        public void CharacterLookupIsGlobalExactAndRejectsSpecialSubstatus()
        {
            CharacterPresenceSnapshot alice = Presence("Alice");
            CharacterPresenceSnapshot special = Presence("Bob") with
            {
                SubStatus = UserSubStatus.Special
            };
            var sessions = new List<PresenceHolder> { new(alice), new(special) };

            Assert.Same(sessions[0], CharacterPresenceRules.FindTarget(
                sessions, "Alice", holder => holder.Presence));
            Assert.Null(CharacterPresenceRules.FindTarget(
                sessions, "alice", holder => holder.Presence));
            Assert.Null(CharacterPresenceRules.FindTarget(
                sessions, "Bob", holder => holder.Presence));
        }

        private static CharacterPresenceSnapshot Presence(string name) => new(
            UserStatus.FieldLobby, 1, 1, UserSubStatus.Normal, name, 0, -1);

        private sealed record PresenceHolder(CharacterPresenceSnapshot Presence);
    }
}
