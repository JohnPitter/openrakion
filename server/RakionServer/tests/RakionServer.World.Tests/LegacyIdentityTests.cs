using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class LegacyIdentityTests
    {
        [Theory]
        [InlineData("ProbeBuddy", true)]
        [InlineData("12345678901", true)]
        [InlineData("", false)]
        [InlineData("123456789012", false)]
        [InlineData("nome com espaco", false)]
        public void BuddyNameValidationMatchesLegacyBoundary(string value, bool expected) =>
            Assert.Equal(expected, LegacyIdentity.IsValidBuddyName(value));

        [Theory]
        [InlineData("ProbeB", true)]
        [InlineData("12345678901", true)]
        [InlineData("123456789012", true)]
        [InlineData("", false)]
        [InlineData("1234567890123", false)]
        [InlineData("nome com espaco", false)]
        public void CharacterNameValidationMatchesClientBoundary(string value, bool expected) =>
            Assert.Equal(expected, LegacyIdentity.IsValidCharacterName(value));

        [Fact]
        public void BuddyNameAck_MatchesLiveProbeWithoutStackGarbage() =>
            Assert.Equal(
                "15000050726F6265427564647900",
                System.Convert.ToHexString(LobbyFrames.BuddyNameAck(0, "ProbeBuddy")));

        [Fact]
        public void CharacterIdentityLookup_MatchesOriginalDbCallbackEnvelope() =>
            Assert.Equal(
                "19000D000074657374320050726F626554776F00",
                System.Convert.ToHexString(LobbyFrames.CharacterIdentityLookup(
                    Database.CharacterIdentityLookupStatus.Success, "test2", "ProbeTwo")));

        [Fact]
        public void CharacterIdentityLookup_NotFoundCarriesEmptyStrings() =>
            Assert.Equal(
                "19000D00020000",
                System.Convert.ToHexString(LobbyFrames.CharacterIdentityLookup(
                    Database.CharacterIdentityLookupStatus.NotFound, "", "")));

        [Fact]
        public void CharacterCreateAck_MatchesLiveSuccessProbeWithoutStackGarbage() =>
            Assert.Equal(
                "12000002000000",
                System.Convert.ToHexString(LobbyFrames.CharacterCreateAck(
                    Database.CharacterCreateStatus.Success, 2)));

        [Theory]
        [InlineData(Database.CharacterCreateStatus.SlotOccupied, "120002")]
        [InlineData(Database.CharacterCreateStatus.DuplicateName, "120004")]
        public void CharacterCreateFailureAck_UsesOriginalStatusAndLogicalLength(
            Database.CharacterCreateStatus status, string expected) =>
            Assert.Equal(expected, System.Convert.ToHexString(LobbyFrames.CharacterCreateAck(status, 0)));

        [Fact]
        public void CharacterStateClearAck_MatchesDecompiledSuccessLayout() =>
            Assert.Equal(
                "1B0000709400007500260000",
                System.Convert.ToHexString(LobbyFrames.CharacterStateClearAck(
                    new Database.CharacterStateClearResult(
                        Database.CharacterStateClearStatus.Success, 38000, 117, 38))));

        [Fact]
        public void CharacterStateClearAck_AppendsOriginalPresentList() =>
            Assert.Equal(
                "1B000070940000750026000168040000",
                System.Convert.ToHexString(LobbyFrames.CharacterStateClearAck(
                    new Database.CharacterStateClearResult(
                        Database.CharacterStateClearStatus.Success, 38000, 117, 38, [1128]))));

        [Theory]
        [InlineData(Database.CharacterStateClearStatus.InvalidCouponItem, "1B0014")]
        [InlineData(Database.CharacterStateClearStatus.CouponNotForCash, "1B0015")]
        [InlineData(Database.CharacterStateClearStatus.CouponDefinitionMissing, "1B0016")]
        public void CharacterStateClearAck_PreservesCouponValidationStatus(
            Database.CharacterStateClearStatus status, string expected) =>
            Assert.Equal(expected, System.Convert.ToHexString(
                LobbyFrames.CharacterStateClearAck(new Database.CharacterStateClearResult(status))));

        [Fact]
        public void CharacterRenameAck_MatchesDecompiledSuccessLayout() =>
            Assert.Equal(
                "1C0000B888000050726F626552656E616D650000",
                System.Convert.ToHexString(LobbyFrames.CharacterRenameAck(
                    new Database.CharacterRenameResult(
                        Database.CharacterRenameStatus.Success, 35000, "ProbeRename"))));

        [Fact]
        public void CharacterRenameAck_AppendsOriginalPresentList() =>
            Assert.Equal(
                "1C0000B888000050726F626552656E616D65000168040000",
                System.Convert.ToHexString(LobbyFrames.CharacterRenameAck(
                    new Database.CharacterRenameResult(
                        Database.CharacterRenameStatus.Success, 35000, "ProbeRename", [1128]))));

        [Theory]
        [InlineData(Database.CharacterRenameStatus.DuplicateName, "1C0001")]
        [InlineData(Database.CharacterRenameStatus.InsufficientCash, "1C0002")]
        [InlineData(Database.CharacterRenameStatus.InvalidCouponItem, "1C0014")]
        public void CharacterRenameAck_UsesOriginalShortFailureLayout(
            Database.CharacterRenameStatus status, string expected) =>
            Assert.Equal(expected, System.Convert.ToHexString(
                LobbyFrames.CharacterRenameAck(new Database.CharacterRenameResult(status))));
    }
}
