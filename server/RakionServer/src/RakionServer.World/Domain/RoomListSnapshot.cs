namespace RakionServer.World.Domain
{
    public readonly record struct RoomListSnapshot(
        ushort FieldId,
        bool HasPassword,
        bool InGame,
        byte MapId,
        byte Mode,
        byte MinLevel,
        byte MaxLevel,
        byte LevelRangeCode,
        byte Round,
        byte MaxRounds,
        byte PlayerCount,
        byte MaxPlayers,
        int MasterUserId,
        ushort MasterSeat,
        string Name,
        ushort Marker);

    public sealed partial class Field
    {
        public RoomListSnapshot CaptureRoomListSnapshot()
        {
            lock (SyncRoot)
            {
                return new RoomListSnapshot(
                    (ushort)Id,
                    !string.IsNullOrEmpty(Password),
                    State == 2,
                    MapId,
                    Mode,
                    MinLevel,
                    MaxLevel,
                    LevelRangeCode,
                    Round,
                    MaxRounds,
                    (byte)Count,
                    MaxPlayers,
                    Master?.Game?.UserId ?? 0,
                    (ushort)(MasterSlot < 0 ? 0 : MasterSlot),
                    Name,
                    0);
            }
        }
    }
}
