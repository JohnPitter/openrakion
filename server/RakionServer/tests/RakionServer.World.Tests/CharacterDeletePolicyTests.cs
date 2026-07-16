using System;
using System.IO;
using System.Threading.Tasks;
using RakionServer.World;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CharacterDeletePolicyTests
    {
        [Fact]
        public void ActiveCharacter_IsRejectedFirst()
        {
            var decision = Evaluate(level: 1, active: true);

            Assert.Equal(CharacterDeleteAction.Reject, decision.Action);
            Assert.Equal(CharacterDeleteResult.ActiveCharacter, decision.Result);
        }

        [Fact]
        public void UsedCharacterUnderSevenDays_IsRejected()
        {
            var decision = Evaluate(level: 1, used: true, ageDays: 6);

            Assert.Equal(CharacterDeleteAction.Reject, decision.Action);
            Assert.Equal(CharacterDeleteResult.TooYoung, decision.Result);
        }

        [Fact]
        public void NeverUsedYoungCharacter_CanBeHardDeleted()
        {
            var decision = Evaluate(level: 14, used: false, ageDays: 0);

            Assert.Equal(CharacterDeleteAction.HardDelete, decision.Action);
            Assert.Equal(CharacterDeleteResult.Success, decision.Result);
        }

        [Fact]
        public void LowLevelCharacter_DoesNotRequireDeleteKey()
        {
            var decision = Evaluate(level: 14, storedKey: "stored", providedKey: "wrong");

            Assert.Equal(CharacterDeleteAction.HardDelete, decision.Action);
        }

        [Theory]
        [InlineData(false, "")]
        [InlineData(false, "old-key")]
        [InlineData(true, "")]
        public void ProtectedCharacter_RequestsNewKeyWhenMissingOrExpired(
            bool keyIsRecent, string providedKey)
        {
            var decision = Evaluate(level: 15, keyIsRecent: keyIsRecent, providedKey: providedKey);

            Assert.Equal(CharacterDeleteAction.IssueKey, decision.Action);
            Assert.Equal(CharacterDeleteResult.DeleteKeySent, decision.Result);
        }

        [Fact]
        public void ProtectedCharacter_RejectsWrongRecentKey()
        {
            var decision = Evaluate(
                level: 15, keyIsRecent: true, storedKey: "right", providedKey: "wrong");

            Assert.Equal(CharacterDeleteAction.Reject, decision.Action);
            Assert.Equal(CharacterDeleteResult.InvalidKey, decision.Result);
        }

        [Fact]
        public void ProtectedCharacter_WithMatchingRecentKey_IsSoftDeleted()
        {
            var decision = Evaluate(
                level: 15, keyIsRecent: true, storedKey: "right", providedKey: "right");

            Assert.Equal(CharacterDeleteAction.SoftDelete, decision.Action);
            Assert.Equal(CharacterDeleteResult.Success, decision.Result);
        }

        [Fact]
        public async Task PickupNotifier_WritesMessageWithoutLoggingDeleteKey()
        {
            string root = Path.Combine(Path.GetTempPath(), "rakion-delete-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string template = Path.Combine(root, "deletion.txt");
                await File.WriteAllTextAsync(template, "Character {1}; key {0}");
                var config = new WorldConfig.CharacterDeleteConfig
                {
                    Enabled = true,
                    Sender = "admin@example.test",
                    Subject = "Rakion",
                    BodyFileName = template,
                    PickupFolder = Path.Combine(root, "pickup"),
                    BaseDirectory = root
                };
                var notifier = new CharacterDeletePickupNotifier(config);

                bool sent = await notifier.SendAsync(new CharacterDeleteOutcome(
                    CharacterDeleteResult.DeleteKeySent, "account", "Hero", "user@example.test", "AB01ab23AB"));

                Assert.True(sent);
                string messagePath = Assert.Single(Directory.GetFiles(config.PickupFolder));
                string message = await File.ReadAllTextAsync(messagePath);
                Assert.Contains("AB01ab23AB", message);
                Assert.Contains("Hero", message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static CharacterDeleteDecision Evaluate(
            byte level, bool used = false, int ageDays = 30, bool active = false,
            bool keyIsRecent = false, string storedKey = "", string providedKey = "") =>
            CharacterDeletePolicy.Evaluate(new CharacterDeleteContext(
                level, used, ageDays, active, keyIsRecent, storedKey), providedKey);
    }
}
