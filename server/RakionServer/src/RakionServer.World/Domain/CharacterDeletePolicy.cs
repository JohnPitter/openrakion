using System;
using RakionServer.World.Database;

namespace RakionServer.World.Domain
{
    public enum CharacterDeleteAction
    {
        Reject,
        HardDelete,
        IssueKey,
        SoftDelete
    }

    public readonly record struct CharacterDeleteDecision(
        CharacterDeleteAction Action, CharacterDeleteResult Result);

    public readonly record struct CharacterDeleteContext(
        byte Level, bool Used, int AgeDays, bool Active, bool KeyIsRecent, string StoredKey);

    public static class CharacterDeletePolicy
    {
        public const byte ProtectedLevel = 15;
        public const int MinimumUsedCharacterAgeDays = 7;

        public static CharacterDeleteDecision Evaluate(
            CharacterDeleteContext context, string providedKey)
        {
            if (context.Active)
                return Reject(CharacterDeleteResult.ActiveCharacter);
            if (context.Used && context.AgeDays < MinimumUsedCharacterAgeDays)
                return Reject(CharacterDeleteResult.TooYoung);
            if (context.Level < ProtectedLevel)
                return Accept(CharacterDeleteAction.HardDelete);
            if (!context.KeyIsRecent || string.IsNullOrEmpty(providedKey))
                return Accept(CharacterDeleteAction.IssueKey, CharacterDeleteResult.DeleteKeySent);
            if (!string.Equals(context.StoredKey, providedKey, StringComparison.Ordinal))
                return Reject(CharacterDeleteResult.InvalidKey);
            return Accept(CharacterDeleteAction.SoftDelete);
        }

        private static CharacterDeleteDecision Accept(
            CharacterDeleteAction action,
            CharacterDeleteResult result = CharacterDeleteResult.Success) => new(action, result);

        private static CharacterDeleteDecision Reject(CharacterDeleteResult result) =>
            new(CharacterDeleteAction.Reject, result);
    }
}
