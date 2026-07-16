using System;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        private ChatModerationEngine BuildChatModeration()
        {
            WorldConfig.ChatConfig chat = _cfg.Chat;
            var settings = new ChatModerationSettings(
                chat.Enabled, chat.Burst, TimeSpan.FromSeconds(chat.WindowSeconds),
                chat.RepeatLimit, TimeSpan.FromSeconds(chat.RepeatWindowSeconds),
                TimeSpan.FromSeconds(chat.AutoMuteSeconds));
            var rules = ChatModerationEngine.LoadRules(chat.AbuseFile);
            if (chat.Enabled && rules.Count == 0)
                Log.Warn("chat", "moderação ativa sem regras em '{0}'", chat.AbuseFile);
            else if (chat.Enabled)
                Log.Ok("chat", "moderação ativa com {0} regra(s)", rules.Count);
            return new ChatModerationEngine(settings, rules);
        }

        public bool ModerateChat(
            ClientSession sender, ClientSession? recipient, ChatScope scope,
            string text, out string moderated)
        {
            if (_cfg.Chat.Enabled && recipient != null &&
                recipient.ChatState.BlockedAccounts.Contains(sender.UserId))
            {
                var blocked = new ChatModerationDecision(
                    ChatModerationAction.Blocked, text, "recipient-block");
                RecordChatDecision(sender, recipient, scope, blocked, text.Length);
                moderated = text;
                return false;
            }

            ChatModerationDecision decision = _chatModeration.Evaluate(
                sender.ChatState, scope, text, DateTime.UtcNow, Environment.TickCount64);
            moderated = decision.Text;
            if (decision.Action != ChatModerationAction.Allowed)
                RecordChatDecision(sender, recipient, scope, decision, text.Length);
            return decision.Allowed;
        }

        private void RecordChatDecision(
            ClientSession sender, ClientSession? recipient, ChatScope scope,
            ChatModerationDecision decision, int originalLength)
        {
            if (decision.AutoMuteUntil.HasValue)
                _ = _db.SaveAutomaticMuteAsync(
                    sender.UserId, decision.AutoMuteUntil.Value, decision.Rule);
            _ = _db.AuditChatDecisionAsync(sender.UserId, recipient?.UserId ?? "",
                scope, decision, originalLength);
            Log.Warn("chat", "sender='{0}' target='{1}' scope={2} action={3} rule='{4}'",
                sender.UserId, recipient?.UserId ?? "", scope, decision.Action, decision.Rule);
        }
    }
}
