namespace RakionServer.World.Domain
{
    public enum GmFieldEntryStatus : byte
    {
        Success = 0,
        OutOfRange = 1,
        Free = 2,
    }

    public readonly record struct GmFieldEntrySnapshot(
        GmFieldEntryStatus Status,
        ushort FieldId,
        string RoomName = "",
        string CreatorCharacter = "");
}
