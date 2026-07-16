using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public class GmAuthorizationTests
    {
        [Theory]
        [InlineData(0, false, true, UserStatus.Lobby)]
        [InlineData(0, true, true, UserStatus.Lobby)]
        [InlineData(1, false, true, UserStatus.Lobby)]
        [InlineData(1, true, false, UserStatus.Lobby)]
        [InlineData(1, true, true, UserStatus.LobbyGm)]
        public void ChannelNeverCreatesAuthority(
            int authority, bool enabled, bool special, byte expected)
        {
            Assert.Equal(expected,
                GmAuthorization.LobbyStatus(authority, enabled, special));
        }

        [Theory]
        [InlineData(0, true, false)]
        [InlineData(1, false, false)]
        [InlineData(1, true, false)]
        [InlineData(2, true, false)]
        [InlineData(3, true, true)]
        public void PermissionRequiresAuthorityAndFeatureFlag(
            int authority, bool enabled, bool expected)
        {
            Assert.Equal(expected, GmAuthorization.IsAllowed(
                authority, enabled, GmPermission.ServerLock));
        }

        [Theory]
        [InlineData(1, GmPermission.VariablesRead, true)]
        [InlineData(1, GmPermission.VariablesWrite, false)]
        [InlineData(2, GmPermission.VariablesWrite, true)]
        [InlineData(2, GmPermission.ClientHashWrite, false)]
        [InlineData(3, GmPermission.ClientHashWrite, true)]
        public void RolesHaveExplicitPermissions(
            int authority, GmPermission permission, bool expected) =>
            Assert.Equal(expected, GmAuthorization.IsAllowed(authority, true, permission));
    }
}
