using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class AdminNoticePolicyTests
    {
        [Theory]
        [InlineData(0, UserStatus.FieldLobby, true)]
        [InlineData(0, UserStatus.InField, true)]
        [InlineData(2, UserStatus.FieldLobby, true)]
        [InlineData(2, UserStatus.InField, false)]
        [InlineData(3, UserStatus.FieldLobby, false)]
        [InlineData(3, UserStatus.InField, true)]
        [InlineData(1, UserStatus.InField, false)]
        public void AppliesOriginalScopeFilter(byte scope, byte status, bool expected)
        {
            var audience = new AdminNoticeAudience(scope, "");
            var recipient = new AdminNoticeRecipient(status, "Player", true, true);

            Assert.Equal(expected, audience.Includes(recipient));
        }

        [Fact]
        public void NamedAudienceRequiresExactCharacterName()
        {
            var audience = new AdminNoticeAudience(3, "Player");

            Assert.True(audience.Includes(new(3, "Player", true, true)));
            Assert.False(audience.Includes(new(3, "player", true, true)));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void RequiresBothOriginalFieldHandles(bool inField, bool secondary)
        {
            var audience = new AdminNoticeAudience(3, "");

            Assert.False(audience.Includes(new(3, "Player", inField, secondary)));
        }
    }
}
