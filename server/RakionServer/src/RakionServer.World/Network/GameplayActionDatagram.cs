using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct GameplayActionHeader(ushort Type, uint Sequence, byte SourceSlot);

    public enum PlayerActionState : byte
    {
        Normal = 0,
        Attack = 1,
        Damage = 2,
        NoState = 3
    }

    public readonly record struct GameplayMoveAction
    {
        public GameplayActionHeader Header { get; init; }
        public ushort DeltaMilliseconds { get; init; }
        public byte SourceEcho { get; init; }
        public PlayerActionState State { get; init; }
        public byte ActionCode { get; init; }
        public short PositionX { get; init; }
        public short PositionY { get; init; }
        public short PositionZ { get; init; }
        public short AngleWord { get; init; }
        public byte AngleByte { get; init; }
        public short ViewRotationX { get; init; }
        public short ViewRotationY { get; init; }
        public short ViewRotationZ { get; init; }
    }

    public static class GameplayActionDatagram
    {
        public const ushort MoveType = 0x030a;
        public const ushort KeyStateType = 0x030f;
        public const ushort AttackType = 0x0311;
        public const int MoveSize = 26;
        public const int KeyStateSize = 14;
        public const int AttackSize = 10;
        public const int ExtendedAttackSize = 12;

        public static bool TryParseHeader(ReadOnlySpan<byte> packet, out GameplayActionHeader header)
        {
            header = default;
            if (packet.Length < 7) return false;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(packet);
            if (!HasValidSize(type, packet.Length)) return false;
            header = new GameplayActionHeader(
                type,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6]);
            return true;
        }

        public static bool TryParseMove(ReadOnlySpan<byte> packet, out GameplayMoveAction action)
        {
            action = default;
            if (!TryParseHeader(packet, out var header) || header.Type != MoveType) return false;
            byte packedState = packet[9];
            action = new GameplayMoveAction
            {
                Header = header,
                DeltaMilliseconds = ReadU16(packet, 7),
                SourceEcho = (byte)(packedState & 0x1f),
                State = (PlayerActionState)((packedState >> 5) & 0x03),
                ActionCode = packet[10],
                PositionX = ReadI16(packet, 11),
                PositionY = ReadI16(packet, 13),
                PositionZ = ReadI16(packet, 15),
                AngleWord = ReadI16(packet, 17),
                AngleByte = packet[19],
                ViewRotationX = ReadI16(packet, 20),
                ViewRotationY = ReadI16(packet, 22),
                ViewRotationZ = ReadI16(packet, 24)
            };
            return true;
        }

        private static bool HasValidSize(ushort type, int size) => type switch
        {
            MoveType => size == MoveSize,
            KeyStateType => size == KeyStateSize,
            AttackType => size is AttackSize or ExtendedAttackSize,
            _ => false
        };

        private static ushort ReadU16(ReadOnlySpan<byte> packet, int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(packet[offset..]);

        private static short ReadI16(ReadOnlySpan<byte> packet, int offset) =>
            BinaryPrimitives.ReadInt16LittleEndian(packet[offset..]);
    }
}
