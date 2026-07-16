namespace RakionServer.World.Domain
{
    public readonly record struct RoomListQuery(
        byte MaxCount,
        ushort Cursor,
        bool Forward,
        byte ModeMask,
        bool BypassEligibility)
    {
        public bool IncludesMode(byte mode) =>
            mode < 5 && (ModeMask & (1 << mode)) != 0;

        public bool Includes(RoomListSnapshot room, byte characterLevel) =>
            room.FieldId != 0 &&
            !room.InGame &&
            IncludesMode(room.Mode) &&
            (BypassEligibility || IsEligible(room, characterLevel));

        public static bool IsEligible(RoomListSnapshot room, byte characterLevel) =>
            !room.InGame &&
            room.PlayerCount < room.MaxPlayers &&
            characterLevel >= room.MinLevel &&
            characterLevel <= room.MaxLevel;
    }
}
