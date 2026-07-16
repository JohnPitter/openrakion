using System;
using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    public readonly record struct CharacterPresenceSnapshot(
        byte Status,
        int GameInfoId,
        int CharacterId,
        byte SubStatus,
        string CharacterName,
        int ChannelId,
        int FieldId);

    public readonly record struct WorldLocation(byte Kind, ushort Id);

    public static class CharacterPresenceRules
    {
        public static bool HasSelectedCharacter(CharacterPresenceSnapshot presence) =>
            presence.GameInfoId > 0 && presence.CharacterId > 0;

        public static T? FindTarget<T>(
            IEnumerable<T> candidates,
            string characterName,
            Func<T, CharacterPresenceSnapshot> snapshot) where T : class
        {
            foreach (T candidate in candidates)
            {
                CharacterPresenceSnapshot presence = snapshot(candidate);
                if (IsEligibleTarget(presence) &&
                    string.Equals(presence.CharacterName, characterName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        public static bool TryGetLocation(
            CharacterPresenceSnapshot presence, out WorldLocation location)
        {
            if (presence.Status == UserStatus.FieldLobby)
            {
                location = new WorldLocation(0, unchecked((byte)presence.ChannelId));
                return true;
            }

            if (presence.Status == UserStatus.InField)
            {
                location = new WorldLocation(1, unchecked((ushort)presence.FieldId));
                return true;
            }

            location = default;
            return false;
        }

        private static bool IsEligibleTarget(CharacterPresenceSnapshot presence) =>
            presence.Status != UserStatus.Disconnected &&
            HasSelectedCharacter(presence) &&
            presence.SubStatus != UserSubStatus.Special;
    }
}
