using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace RakionServer.World.Network
{
    public enum GameplayNpcCreationKind : byte
    {
        General,
        MasterGolem,
        Map
    }

    public readonly record struct GameplayEntityPlacement(
        float X, float Y, float Z, float Heading, float Pitch, float Bank);

    public readonly record struct GameplayNpcCreation(
        GameplayNpcCreationKind Kind,
        uint Sequence,
        byte SourceSeat,
        byte OwnerOrHostSeat,
        byte NpcOrTeamIndex,
        ushort EntityField,
        GameplayEntityPlacement Placement,
        int InitBlobOffset,
        int InitBlobLength);

    public readonly record struct GameplayEntityState(
        uint Sequence,
        byte SourceSeat,
        ushort TimingOrState,
        byte EntityKind,
        byte Group,
        byte Index,
        short X,
        short Y,
        short Z,
        short Heading);

    public readonly record struct GameplayMapItemState(byte Index, byte State);

    public readonly record struct GameplayMapNpcCreateRequest(
        uint Sequence,
        byte SourceSeat,
        byte TargetSeat,
        byte EntityKind,
        byte MapIndex);

    public readonly record struct GameplayMapItemSnapshot(
        uint Sequence,
        byte SourceSeat,
        IReadOnlyList<GameplayMapItemState> Items);

    public static partial class GameplayPeerDatagramCodec
    {
        public static bool TryParseNpcCreation(
            ReadOnlySpan<byte> packet, out GameplayNpcCreation value)
        {
            value = default;
            if (packet.Length < 35) return false;
            GameplayNpcCreationKind? kind = CreationKind(
                BinaryPrimitives.ReadUInt16LittleEndian(packet));
            if (kind == null) return false;

            value = new GameplayNpcCreation(
                kind.Value,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6], packet[7], packet[8],
                BinaryPrimitives.ReadUInt16LittleEndian(packet[9..]),
                ReadPlacement(packet[11..]),
                35,
                packet.Length - 35);
            return true;
        }

        public static bool TryParseEntityState(
            ReadOnlySpan<byte> packet, out GameplayEntityState value)
        {
            value = default;
            if (packet.Length != 20 ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != EntityStateType ||
                packet[9] is not (2 or 3 or 4))
                return false;

            value = new GameplayEntityState(
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6],
                BinaryPrimitives.ReadUInt16LittleEndian(packet[7..]),
                packet[9], packet[10], packet[11],
                BinaryPrimitives.ReadInt16LittleEndian(packet[12..]),
                BinaryPrimitives.ReadInt16LittleEndian(packet[14..]),
                BinaryPrimitives.ReadInt16LittleEndian(packet[16..]),
                BinaryPrimitives.ReadInt16LittleEndian(packet[18..]));
            return true;
        }

        public static bool TryParseMapItemSnapshot(
            ReadOnlySpan<byte> packet, out GameplayMapItemSnapshot value)
        {
            value = default;
            if (packet.Length < 8 ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != MapItemSnapshotType ||
                !IsValidMapItemSnapshot(packet))
                return false;

            int count = packet[7];
            var items = new GameplayMapItemState[count];
            for (int index = 0; index < count; index++)
                items[index] = new GameplayMapItemState(
                    packet[8 + index * 2], packet[9 + index * 2]);
            value = new GameplayMapItemSnapshot(
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6], items);
            return true;
        }

        public static bool TryParseMapNpcCreateRequest(
            ReadOnlySpan<byte> packet, out GameplayMapNpcCreateRequest value)
        {
            value = default;
            if (packet.Length != 10 ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != MapNpcActionType ||
                packet[8] != 3)
                return false;
            value = new GameplayMapNpcCreateRequest(
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6], packet[7], packet[8], packet[9]);
            return true;
        }

        private static GameplayNpcCreationKind? CreationKind(ushort type) => type switch
        {
            GeneralNpcCreateType => GameplayNpcCreationKind.General,
            MasterGolemCreateType => GameplayNpcCreationKind.MasterGolem,
            MapNpcCreateType => GameplayNpcCreationKind.Map,
            _ => null
        };

        private static GameplayEntityPlacement ReadPlacement(ReadOnlySpan<byte> value) => new(
            ReadSingleLittleEndian(value),
            ReadSingleLittleEndian(value[4..]),
            ReadSingleLittleEndian(value[8..]),
            ReadSingleLittleEndian(value[12..]),
            ReadSingleLittleEndian(value[16..]),
            ReadSingleLittleEndian(value[20..]));
    }
}
