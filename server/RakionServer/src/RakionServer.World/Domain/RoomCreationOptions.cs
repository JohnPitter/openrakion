namespace RakionServer.World.Domain
{
    public sealed record RoomCreationOptions
    {
        public required string Name { get; init; }
        public string Password { get; init; } = "";
        public string Description { get; init; } = "";
        public byte MapId { get; init; }
        public byte Mode { get; init; }
        public byte Rounds { get; init; } = 1;
        public ushort DurationSeconds { get; init; } = 432;
        public byte FragLimit { get; init; }
        public byte MinLevel { get; init; } = 1;
        public byte MaxLevel { get; init; } = 99;
        public byte LevelRangeCode { get; init; }
        public byte? CapacityOverride { get; init; }
        public bool Searchable { get; init; } = true;

        public byte Capacity => CapacityOverride is >= 1 and <= 20
            ? CapacityOverride.Value
            : Mode == 0 ? (byte)8 : (byte)12;
    }
}
