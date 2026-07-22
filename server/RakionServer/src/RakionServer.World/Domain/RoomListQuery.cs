namespace RakionServer.World.Domain
{
    public readonly record struct RoomListQuery(
        byte MaxCount,
        ushort Cursor,
        bool Forward,
        byte ModeMask,
        bool IncludeUnavailable)
    {
        public static RoomListQuery DefaultAvailable => new(10, 0, true, 0x1f, false);

        public bool IncludesMode(byte mode) =>
            mode < 5 && (ModeMask & (1 << mode)) != 0;

        public bool Includes(RoomListSnapshot room, byte characterLevel) =>
            room.FieldId != 0 &&
            IncludesMode(room.Mode) &&
            (IncludeUnavailable || IsAvailableTo(room, characterLevel));

        public static bool IsAvailableTo(RoomListSnapshot room, byte characterLevel) =>
            room.PlayerCount < room.MaxPlayers &&
            characterLevel >= room.MinLevel &&
            characterLevel <= room.MaxLevel &&
            (room.Mode != 0 || !room.InGame);
    }
}
