using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RakionServer.Common
{
    public enum ChatScope : byte
    {
        Room = 0,
        Field = 1,
        Whisper = 2,
        Sms = 3,
        Channel = 4
    }

    public enum ChatModerationAction : byte
    {
        Allowed = 0,
        Filtered = 1,
        Invalid = 2,
        RateLimited = 3,
        Muted = 4,
        Blocked = 5
    }

    public sealed record ChatModerationSettings(
        bool Enabled, int Burst, TimeSpan Window, int RepeatLimit,
        TimeSpan RepeatWindow, TimeSpan AutoMute);

    public sealed record ChatAbuseRule(string Pattern, string Replacement);

    public sealed record ChatModerationDecision(
        ChatModerationAction Action, string Text, string Rule = "",
        DateTime? AutoMuteUntil = null)
    {
        public bool Allowed => Action is ChatModerationAction.Allowed or
            ChatModerationAction.Filtered;
    }

    public sealed class ChatSessionState
    {
        private readonly Dictionary<ChatScope, Queue<long>> _messages = new();
        private string _lastText = "";
        private long _lastTextAt;
        private int _repeatCount;
        public DateTime MutedUntilUtc { get; private set; }
        public HashSet<string> BlockedAccounts { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public void Load(DateTime mutedUntilUtc, IEnumerable<string> blockedAccounts)
        {
            MutedUntilUtc = mutedUntilUtc;
            BlockedAccounts.Clear();
            foreach (string account in blockedAccounts) BlockedAccounts.Add(account);
        }

        public bool Consume(ChatScope scope, long now, long windowMs, int burst)
        {
            if (!_messages.TryGetValue(scope, out Queue<long>? queue))
                _messages[scope] = queue = new Queue<long>();
            while (queue.Count > 0 && now - queue.Peek() >= windowMs) queue.Dequeue();
            if (queue.Count >= burst) return false;
            queue.Enqueue(now);
            return true;
        }

        public bool IsRepeated(string text, long now, long windowMs, int limit)
        {
            if (!string.Equals(_lastText, text, StringComparison.Ordinal) ||
                now - _lastTextAt >= windowMs)
                _repeatCount = 0;
            _lastText = text;
            _lastTextAt = now;
            return ++_repeatCount >= limit;
        }

        public void Mute(DateTime untilUtc) => MutedUntilUtc = untilUtc;
    }

    public sealed class ChatModerationEngine
    {
        public const int MaxTextLength = 128;
        private readonly ChatModerationSettings _settings;
        private readonly (Regex Regex, string Replacement, string Rule)[] _rules;

        public ChatModerationEngine(
            ChatModerationSettings settings, IReadOnlyList<ChatAbuseRule> rules)
        {
            _settings = settings;
            _rules = new (Regex, string, string)[rules.Count];
            for (int i = 0; i < rules.Count; i++)
                _rules[i] = (new Regex(Regex.Escape(rules[i].Pattern),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    rules[i].Replacement, rules[i].Pattern);
        }

        public ChatModerationDecision Evaluate(
            ChatSessionState state, ChatScope scope, string text,
            DateTime nowUtc, long monotonicMs)
        {
            if (!_settings.Enabled) return new(ChatModerationAction.Allowed, text);
            if (state.MutedUntilUtc > nowUtc)
                return new(ChatModerationAction.Muted, text, "active-mute");
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength ||
                HasControlCharacters(text))
                return new(ChatModerationAction.Invalid, text, "invalid-text");
            if (!state.Consume(scope, monotonicMs,
                    (long)_settings.Window.TotalMilliseconds, _settings.Burst))
                return AutoMute(state, text, nowUtc, "rate-limit");
            string normalized = NormalizeForRepeat(text);
            if (state.IsRepeated(normalized, monotonicMs,
                    (long)_settings.RepeatWindow.TotalMilliseconds, _settings.RepeatLimit))
                return AutoMute(state, text, nowUtc, "repeat-limit");
            return ApplyRules(text);
        }

        private ChatModerationDecision ApplyRules(string text)
        {
            string filtered = text;
            string matched = "";
            foreach (var rule in _rules)
            {
                if (!rule.Regex.IsMatch(filtered)) continue;
                filtered = rule.Regex.Replace(filtered, rule.Replacement);
                if (matched.Length == 0) matched = rule.Rule;
            }
            if (filtered.Length > MaxTextLength)
                return new(ChatModerationAction.Invalid, text, "filtered-length");
            return matched.Length == 0
                ? new(ChatModerationAction.Allowed, text)
                : new(ChatModerationAction.Filtered, filtered, matched);
        }

        private ChatModerationDecision AutoMute(
            ChatSessionState state, string text, DateTime nowUtc, string rule)
        {
            DateTime until = nowUtc.Add(_settings.AutoMute);
            state.Mute(until);
            return new ChatModerationDecision(
                ChatModerationAction.RateLimited, text, rule, until);
        }

        private static bool HasControlCharacters(string text)
        {
            foreach (char value in text)
                if (char.IsControl(value) && value != '\t') return true;
            return false;
        }

        private static string NormalizeForRepeat(string text) =>
            Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");

        public static IReadOnlyList<ChatAbuseRule> LoadRules(string path)
        {
            var rules = new List<ChatAbuseRule>();
            if (!File.Exists(path)) return rules;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';') continue;
                string[] columns = line.Split('\t', 2);
                if (columns.Length != 2 || columns[0].Length == 0 || !seen.Add(columns[0])) continue;
                rules.Add(new ChatAbuseRule(columns[0], columns[1]));
            }
            return rules;
        }
    }
}
