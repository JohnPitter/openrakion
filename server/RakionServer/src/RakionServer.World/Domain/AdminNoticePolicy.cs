using System;

namespace RakionServer.World.Domain
{
    public readonly record struct AdminNoticeAudience(byte Scope, string CharacterName)
    {
        public bool Includes(AdminNoticeRecipient recipient)
        {
            if (!recipient.InField || !recipient.HasFieldSecondary)
                return false;
            if (CharacterName.Length > 0 &&
                !string.Equals(CharacterName, recipient.CharacterName, StringComparison.Ordinal))
                return false;

            return Scope switch
            {
                0 => true,
                2 => recipient.Status == UserStatus.FieldLobby,
                3 => recipient.Status == UserStatus.InField,
                _ => false
            };
        }
    }

    public readonly record struct AdminNoticeRecipient(
        byte Status,
        string CharacterName,
        bool InField,
        bool HasFieldSecondary);
}
