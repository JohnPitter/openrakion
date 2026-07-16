using System;

namespace RakionServer.World.Domain
{
    public readonly record struct FieldRoomInfoSlotSnapshot(
        ushort UserId, byte Status, byte Auth, byte Vote);

    public sealed record FieldRoomInfoSnapshot
    {
        public ushort Id { get; init; }
        public byte Status { get; init; }
        public string CreatorCharacter { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public byte MinLevel { get; init; }
        public byte MaxLevel { get; init; }
        public byte Basic { get; init; }
        public byte Map { get; init; }
        public byte Mode { get; init; }
        public byte Boss { get; init; }
        public uint Tunneling { get; init; }
        public byte OnVote { get; init; }
        public byte VotePosition { get; init; }
        public byte BanSlot { get; init; }
        public FieldRoomInfoSlotSnapshot[] Slots { get; init; } = EmptySlots();

        public static FieldRoomInfoSnapshot Empty(ushort id) => new() { Id = id };

        public static FieldRoomInfoSnapshot From(Field field)
        {
            var slots = new FieldRoomInfoSlotSnapshot[field.Slots.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                PlayerRec record = field.Slots[index];
                slots[index] = new FieldRoomInfoSlotSnapshot(
                    record.Session?.Slot ?? (ushort)0,
                    record.State,
                    record.Session?.SubStatus ?? (byte)0,
                    record.VoteState);
            }

            return new FieldRoomInfoSnapshot
            {
                Id = checked((ushort)field.Id),
                Status = field.State,
                CreatorCharacter = field.CreatorCharacterName,
                Title = field.Name,
                Password = field.Password,
                MinLevel = field.MinLevel,
                MaxLevel = field.MaxLevel,
                Basic = field.LevelRangeCode,
                Map = field.MapId,
                Mode = field.Mode,
                Boss = field.MasterSlot >= 0 ? checked((byte)field.MasterSlot) : (byte)0,
                Tunneling = field.HasTunnelingClient ? 1u : 0u,
                OnVote = field.VoteActive ? (byte)1 : (byte)0,
                VotePosition = field.VotePenaltySlot,
                BanSlot = field.VoteTargetSeat,
                Slots = slots
            };
        }

        private static FieldRoomInfoSlotSnapshot[] EmptySlots() =>
            new FieldRoomInfoSlotSnapshot[0x14];
    }
}
