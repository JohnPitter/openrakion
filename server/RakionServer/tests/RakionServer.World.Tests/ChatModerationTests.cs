using System;
using System.IO;
using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class ChatModerationTests
    {
        private static ChatModerationEngine CreateEngine(
            int burst = 5, int repeatLimit = 3) => new(
                new ChatModerationSettings(true, burst, TimeSpan.FromSeconds(5),
                    repeatLimit, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)),
                [new ChatAbuseRule("badword", "LOVE")]);

        [Fact]
        public void Evaluate_FiltersCaseInsensitivelyWithoutChangingWireScope()
        {
            ChatModerationDecision result = CreateEngine().Evaluate(
                new ChatSessionState(), ChatScope.Field, "a BADWORD here",
                DateTime.UtcNow, 1000);

            Assert.Equal(ChatModerationAction.Filtered, result.Action);
            Assert.Equal("a LOVE here", result.Text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("line\nbreak")]
        public void Evaluate_RejectsEmptyOrControlText(string text)
        {
            ChatModerationDecision result = CreateEngine().Evaluate(
                new ChatSessionState(), ChatScope.Room, text, DateTime.UtcNow, 1000);

            Assert.Equal(ChatModerationAction.Invalid, result.Action);
        }

        [Fact]
        public void Evaluate_RateLimitsPerScopeAndPersistsMuteInState()
        {
            var state = new ChatSessionState();
            ChatModerationEngine engine = CreateEngine(burst: 1, repeatLimit: 5);
            DateTime now = DateTime.UtcNow;

            Assert.True(engine.Evaluate(state, ChatScope.Room, "one", now, 1000).Allowed);
            Assert.True(engine.Evaluate(state, ChatScope.Field, "two", now, 1001).Allowed);
            ChatModerationDecision limited = engine.Evaluate(
                state, ChatScope.Room, "three", now, 1002);
            ChatModerationDecision muted = engine.Evaluate(
                state, ChatScope.Field, "four", now.AddSeconds(1), 2000);

            Assert.Equal(ChatModerationAction.RateLimited, limited.Action);
            Assert.NotNull(limited.AutoMuteUntil);
            Assert.Equal(ChatModerationAction.Muted, muted.Action);
        }

        [Fact]
        public void Evaluate_RepetitionTriggersAutomaticMute()
        {
            var state = new ChatSessionState();
            ChatModerationEngine engine = CreateEngine(repeatLimit: 3);
            DateTime now = DateTime.UtcNow;

            engine.Evaluate(state, ChatScope.Whisper, "same text", now, 1000);
            engine.Evaluate(state, ChatScope.Whisper, " SAME   TEXT ", now, 1100);
            ChatModerationDecision result = engine.Evaluate(
                state, ChatScope.Whisper, "same text", now, 1200);

            Assert.Equal(ChatModerationAction.RateLimited, result.Action);
            Assert.Equal("repeat-limit", result.Rule);
        }

        [Fact]
        public void BlockList_IsCaseInsensitiveAndLoadedFromPersistenceState()
        {
            var state = new ChatSessionState();
            state.Load(DateTime.MinValue, ["BlockedAccount"]);

            Assert.Contains("blockedaccount", state.BlockedAccounts);
        }

        [Fact]
        public void DeploymentRuleFile_LoadsCanonicalDeduplicatedRules()
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../deploy/abusestring.txt"));
            var rules = ChatModerationEngine.LoadRules(path);

            Assert.True(rules.Count >= 25);
            Assert.Contains(rules, rule => rule.Pattern == "fuck" && rule.Replacement == "LOVE");
        }
    }
}
