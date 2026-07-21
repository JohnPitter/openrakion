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

    public readonly record struct GameplaySyncAction
    {
        public GameplayActionHeader Header { get; init; }
        public byte SourceEcho { get; init; }
        public byte LifeState { get; init; }
        public byte PlayerValueA { get; init; }
        public byte AnimatorValue { get; init; }
        public byte PlayerValueB { get; init; }
        public byte ControlMode { get; init; }
        public byte ControlDetail { get; init; }
    }

    public enum PlayerAnimationKind : byte
    {
        Normal = 0,
        Attack = 1,
        Damage = 2
    }

    public readonly record struct GameplayAnimationAction
    {
        public GameplayActionHeader Header { get; init; }
        public byte SourceEcho { get; init; }
        public PlayerAnimationKind Kind { get; init; }
        public byte Argument0 { get; init; }
        public byte Argument1 { get; init; }
        public byte Argument2 { get; init; }
        public bool HasExtendedPayload { get; init; }
    }

    public static class GameplayActionDatagram
    {
        public const ushort MoveType = 0x030a;
        public const ushort SyncType = 0x030f;
        public const ushort AnimationType = 0x0311;
        public const int MoveSize = 26;
        public const int SyncSize = 14;
        public const int AnimationSize = 10;
        public const int ExtendedAnimationSize = 12;

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

        public static bool TryParseSync(ReadOnlySpan<byte> packet, out GameplaySyncAction action)
        {
            action = default;
            if (!TryParseHeader(packet, out var header) || header.Type != SyncType) return false;
            action = new GameplaySyncAction
            {
                Header = header,
                SourceEcho = packet[7],
                LifeState = packet[8],
                PlayerValueA = packet[9],
                AnimatorValue = packet[10],
                PlayerValueB = packet[11],
                ControlMode = packet[12],
                ControlDetail = packet[13]
            };
            return true;
        }

        public static bool TryParseAnimation(
            ReadOnlySpan<byte> packet,
            out GameplayAnimationAction action)
        {
            action = default;
            if (!TryParseHeader(packet, out var header) || header.Type != AnimationType) return false;
            var kind = (PlayerAnimationKind)packet[8];
            if (kind is < PlayerAnimationKind.Normal or > PlayerAnimationKind.Damage) return false;
            bool extended = packet.Length == ExtendedAnimationSize;
            if (kind == PlayerAnimationKind.Damage && !extended) return false;
            action = new GameplayAnimationAction
            {
                Header = header,
                SourceEcho = packet[7],
                Kind = kind,
                Argument0 = packet[9],
                Argument1 = extended ? packet[10] : (byte)0,
                Argument2 = extended ? packet[11] : (byte)0,
                HasExtendedPayload = extended
            };
            return true;
        }

        public static byte[] BuildTunnelPayload(ReadOnlySpan<byte> datagram)
        {
            if (!TryParseHeader(datagram, out _))
                throw new ArgumentException("Datagrama de gameplay inválido.", nameof(datagram));

            byte[] payload = new byte[datagram.Length - 5];
            datagram[..2].CopyTo(payload);
            datagram[7..].CopyTo(payload.AsSpan(2));
            return payload;
        }

        private static bool HasValidSize(ushort type, int size) => type switch
        {
            MoveType => size == MoveSize,
            SyncType => size == SyncSize,
            AnimationType => size is AnimationSize or ExtendedAnimationSize,
            _ => false
        };

        private static ushort ReadU16(ReadOnlySpan<byte> packet, int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(packet[offset..]);

        private static short ReadI16(ReadOnlySpan<byte> packet, int offset) =>
            BinaryPrimitives.ReadInt16LittleEndian(packet[offset..]);
    }
}
