using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public class ClientHashPolicyTests
    {
        private const string Hash1 = "0123456789abcdef0123456789abcdef";
        private const string Hash2 = "fedcba9876543210fedcba9876543210";
        private static readonly ClientHashSettings Enforced = new(true, Hash1, Hash2);

        [Fact]
        public void LoginSelectsHashByModeAndModeFourBypasses()
        {
            Assert.True(ClientHashPolicy.LoginAccepted(0, Hash1, Enforced));
            Assert.True(ClientHashPolicy.LoginAccepted(1, Hash2, Enforced));
            Assert.False(ClientHashPolicy.LoginAccepted(1, Hash1, Enforced));
            Assert.True(ClientHashPolicy.LoginAccepted(4, "", Enforced));
        }

        [Fact]
        public void FieldCheckPreservesOriginalModesAndReasons()
        {
            Assert.Equal<byte?>(0xBB,
                ClientHashPolicy.FieldDisconnectReason(0, false, Hash1, Enforced));
            Assert.Equal<byte?>(0xBC,
                ClientHashPolicy.FieldDisconnectReason(0, true, Hash2, Enforced));
            Assert.Null(ClientHashPolicy.FieldDisconnectReason(0, true, Hash1, Enforced));
            Assert.Null(ClientHashPolicy.FieldDisconnectReason(4, false, "", Enforced));
            Assert.Null(ClientHashPolicy.FieldDisconnectReason(5, false, "", Enforced));
        }

        [Fact]
        public void DisabledPolicyKeepsCompatibilityModeExplicit()
        {
            var disabled = new ClientHashSettings(false, "", "");
            Assert.True(ClientHashPolicy.LoginAccepted(0, "legacy", disabled));
            Assert.Null(ClientHashPolicy.FieldDisconnectReason(0, false, "", disabled));
        }

        [Fact]
        public void EnforcedHashRequiresExactCaseSensitiveThirtyTwoBytes()
        {
            Assert.Equal<byte?>(0xBC,
                ClientHashPolicy.FieldDisconnectReason(0, true, Hash1[..31], Enforced));
            Assert.Equal<byte?>(0xBC,
                ClientHashPolicy.FieldDisconnectReason(0, true, Hash1.ToUpperInvariant(), Enforced));
            Assert.Null(ClientHashPolicy.FieldDisconnectReason(0, true, Hash1, Enforced));
        }
    }
}
