using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public enum GameplayPeerDatagramKind
    {
        ApplicationReliablePush,
        ApplicationReliableAck,
        AddressUpdate,
        TransportAck,
        BadPingStatus,
        ReliableMessage
    }

    public readonly record struct GameplayPeerDatagram(
        GameplayPeerDatagramKind Kind,
        ushort Type,
        uint Sequence,
        byte SourceSeat);

    public readonly record struct GameplayBadPingStatus(
        uint Sequence, byte SourceSeat, byte PlayerSeat, bool IsBad);

    public readonly record struct GameplayEntityEvent(
        uint Sequence,
        byte TransportSourceSeat,
        byte SenderSeat,
        byte Route,
        byte PrimaryEntitySeat,
        byte SecondaryEntitySeat,
        uint EventId,
        int PayloadLength);

    public readonly record struct GameplayPlayerVitals(
        GameplayEntityEvent Envelope, uint PlayerId, float Hp, float Ap);

    public readonly record struct GameplayPlayerDamage(
        GameplayEntityEvent Envelope,
        uint PlayerId,
        byte DamageType,
        byte DamageMotionType,
        ushort Reserved,
        float FirstDamageValue,
        float SecondDamageValue,
        GameplayVector3 FirstVector,
        GameplayVector3 SecondVector);

    public readonly record struct GameplayPlayerDeath(
        GameplayEntityEvent Envelope, GameplayVector3 DeathVector);

    public readonly record struct GameplayUsePotionEvent(
        GameplayEntityEvent Envelope, int PotionKind, int Argument);

    public readonly record struct GameplayVector3(float X, float Y, float Z);

    public readonly record struct GameplaySetWeaponEvent(
        GameplayEntityEvent Envelope, int WeaponSelector, int Argument);

    public readonly record struct GameplayShootWeaponEvent(
        GameplayEntityEvent Envelope,
        GameplayVector3 FirstVector,
        GameplayVector3 SecondVector,
        byte ShootType,
        byte Reserved0,
        byte Reserved1,
        byte Reserved2);

    public readonly record struct GameplayShootShurikenEvent(
        GameplayEntityEvent Envelope,
        GameplayVector3 FirstVector,
        GameplayVector3 SecondVector,
        byte ProjectileCount,
        byte Variant,
        ushort Reserved);

    public readonly record struct GameplayRequestHoldAttackEvent(
        GameplayEntityEvent Envelope,
        uint EntityWord,
        byte EntityIndex,
        byte EntitySubIndex,
        ushort Reserved,
        float MaximumDistance,
        uint Argument);

    public readonly record struct GameplayHoldAttackEvent(
        GameplayEntityEvent Envelope,
        uint EntityWord,
        byte EntityIndex,
        byte EntitySubIndex,
        ushort Reserved0,
        uint Argument,
        byte ActorIndex,
        byte ActorSubIndex,
        ushort Reserved1);

    public static partial class GameplayPeerDatagramCodec
    {
        public const ushort ApplicationReliablePushType = 0x0304;
        public const ushort ApplicationReliableAckType = 0x0305;
        public const ushort AddressUpdateType = 0x0319;
        public const ushort TransportAckType = 0x4000;
        public const ushort GeneralNpcCreateType = 0x8307;
        public const ushort MasterGolemCreateType = 0x8308;
        public const ushort MapNpcCreateType = 0x8309;
        public const ushort EntityStateType = 0x830b;
        public const ushort EntityEventType = 0x830c;
        public const ushort MapNpcActionType = 0x8310;
        public const ushort MapItemSnapshotType = 0x8312;
        public const ushort BadPingType = 0x8313;
        public const ushort GameplayTickType = 0x8315;
        public const uint PlayerDamageEventId = 0x0191000b;
        public const uint PlayerRemainHpEventId = 0x0191000c;
        public const uint PlayerDeathEventId = 0x01910016;
        public const uint RespawnEventId = 0x01910017;
        public const uint SetWeaponEventId = 0x01910006;
        public const uint ShootWeaponEventId = 0x01910007;
        public const uint ShootShurikenEventId = 0x01910008;
        public const uint RequestHoldAttackEventId = 0x01910009;
        public const uint HoldAttackEventId = 0x0191000a;
        public const uint UsePotionEventId = 0x01910025;

        public static bool TryParse(ReadOnlySpan<byte> packet, out GameplayPeerDatagram value)
        {
            value = default;
            if (packet.Length < 6) return false;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(packet);
            if (!TryClassify(type, packet, out var kind)) return false;
            if (type == EntityEventType && !TryParseValidEntityEvent(packet)) return false;
            value = new GameplayPeerDatagram(
                kind,
                type,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6]);
            return true;
        }

        public static bool SourceMatches(
            GameplayPeerDatagram datagram, byte authenticatedSeat) =>
            datagram.SourceSeat == authenticatedSeat ||
            (datagram.Type is ApplicationReliablePushType or ApplicationReliableAckType &&
                datagram.SourceSeat == byte.MaxValue);

        public static bool SourceMatches(
            GameplayPeerDatagram datagram,
            byte authenticatedSeat,
            ReadOnlySpan<byte> packet)
        {
            if (!SourceMatches(datagram, authenticatedSeat)) return false;
            if (datagram.Type == EntityEventType &&
                TryParseEntityEvent(packet, out var entityEvent))
                return entityEvent.SenderSeat == authenticatedSeat;
            if (datagram.Type == MapNpcActionType &&
                TryParseMapNpcCreateRequest(packet, out var request))
                return request.TargetSeat == authenticatedSeat;
            return true;
        }

        public static bool TryParseEntityEvent(
            ReadOnlySpan<byte> packet, out GameplayEntityEvent value)
        {
            value = default;
            if (packet.Length < 19 ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != EntityEventType)
                return false;

            byte route = packet[8];
            if (route is not (1 or 2 or 3 or 4 or 6 or 7)) return false;
            uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(packet[15..]);
            if (payloadLength > int.MaxValue || packet.Length != 19 + (int)payloadLength)
                return false;

            value = new GameplayEntityEvent(
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6],
                packet[7],
                route,
                packet[9],
                packet[10],
                BinaryPrimitives.ReadUInt32LittleEndian(packet[11..]),
                (int)payloadLength);
            return true;
        }

        public static bool TryParsePlayerVitals(
            ReadOnlySpan<byte> packet, out GameplayPlayerVitals value)
        {
            value = default;
            if (!TryParseEntityEvent(packet, out var envelope) ||
                envelope.EventId != PlayerRemainHpEventId || envelope.PayloadLength != 12)
                return false;
            value = new GameplayPlayerVitals(
                envelope,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[19..]),
                ReadSingleLittleEndian(packet[23..]),
                ReadSingleLittleEndian(packet[27..]));
            return true;
        }

        public static bool TryParsePlayerDamage(
            ReadOnlySpan<byte> packet, out GameplayPlayerDamage value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, PlayerDamageEventId, 40, out var envelope))
                return false;
            value = new GameplayPlayerDamage(
                envelope,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[19..]),
                packet[23], packet[24],
                BinaryPrimitives.ReadUInt16LittleEndian(packet[25..]),
                ReadSingleLittleEndian(packet[27..]),
                ReadSingleLittleEndian(packet[31..]),
                ReadVector3(packet[35..]),
                ReadVector3(packet[47..]));
            return true;
        }

        public static bool TryParsePlayerDeath(
            ReadOnlySpan<byte> packet, out GameplayPlayerDeath value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, PlayerDeathEventId, 12, out var envelope))
                return false;
            value = new GameplayPlayerDeath(envelope, ReadVector3(packet[19..]));
            return true;
        }

        public static bool TryParseUsePotion(
            ReadOnlySpan<byte> packet, out GameplayUsePotionEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, UsePotionEventId, 8, out var envelope))
                return false;
            int potionKind = BinaryPrimitives.ReadInt32LittleEndian(packet[19..]);
            if (potionKind is < 0 or > 7) return false;
            value = new GameplayUsePotionEvent(
                envelope,
                potionKind,
                BinaryPrimitives.ReadInt32LittleEndian(packet[23..]));
            return true;
        }

        public static bool TryParseSetWeapon(
            ReadOnlySpan<byte> packet, out GameplaySetWeaponEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, SetWeaponEventId, 8, out var envelope))
                return false;
            value = new GameplaySetWeaponEvent(
                envelope,
                BinaryPrimitives.ReadInt32LittleEndian(packet[19..]),
                BinaryPrimitives.ReadInt32LittleEndian(packet[23..]));
            return true;
        }

        public static bool TryParseShootWeapon(
            ReadOnlySpan<byte> packet, out GameplayShootWeaponEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, ShootWeaponEventId, 28, out var envelope))
                return false;
            value = new GameplayShootWeaponEvent(
                envelope,
                ReadVector3(packet[19..]),
                ReadVector3(packet[31..]),
                packet[43], packet[44], packet[45], packet[46]);
            return true;
        }

        public static bool TryParseShootShuriken(
            ReadOnlySpan<byte> packet, out GameplayShootShurikenEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, ShootShurikenEventId, 28, out var envelope))
                return false;
            value = new GameplayShootShurikenEvent(
                envelope,
                ReadVector3(packet[19..]),
                ReadVector3(packet[31..]),
                packet[43], packet[44],
                BinaryPrimitives.ReadUInt16LittleEndian(packet[45..]));
            return true;
        }

        public static bool TryParseRequestHoldAttack(
            ReadOnlySpan<byte> packet, out GameplayRequestHoldAttackEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, RequestHoldAttackEventId, 16, out var envelope))
                return false;
            value = new GameplayRequestHoldAttackEvent(
                envelope,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[19..]),
                packet[23], packet[24],
                BinaryPrimitives.ReadUInt16LittleEndian(packet[25..]),
                ReadSingleLittleEndian(packet[27..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[31..]));
            return true;
        }

        public static bool TryParseHoldAttack(
            ReadOnlySpan<byte> packet, out GameplayHoldAttackEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, HoldAttackEventId, 16, out var envelope))
                return false;
            value = new GameplayHoldAttackEvent(
                envelope,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[19..]),
                packet[23], packet[24],
                BinaryPrimitives.ReadUInt16LittleEndian(packet[25..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[27..]),
                packet[31], packet[32],
                BinaryPrimitives.ReadUInt16LittleEndian(packet[33..]));
            return true;
        }

        private static bool TryParseExpectedEvent(
            ReadOnlySpan<byte> packet,
            uint eventId,
            int payloadLength,
            out GameplayEntityEvent envelope) =>
            TryParseEntityEvent(packet, out envelope) &&
            envelope.EventId == eventId && envelope.PayloadLength == payloadLength;

        private static bool TryParseValidEntityEvent(ReadOnlySpan<byte> packet)
        {
            if (!TryParseEntityEvent(packet, out var envelope)) return false;
            if (envelope.EventId == UsePotionEventId && !TryParseUsePotion(packet, out _))
                return false;
            return IsValidKnownChristmasEvent(packet, envelope);
        }

        private static GameplayVector3 ReadVector3(ReadOnlySpan<byte> value) =>
            new(
                ReadSingleLittleEndian(value),
                ReadSingleLittleEndian(value[4..]),
                ReadSingleLittleEndian(value[8..]));

        private static float ReadSingleLittleEndian(ReadOnlySpan<byte> value) =>
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value));

        public static bool TryParseBadPing(
            ReadOnlySpan<byte> packet, out GameplayBadPingStatus value)
        {
            value = default;
            if (packet.Length != 9 ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != BadPingType || packet[8] > 1)
                return false;
            value = new GameplayBadPingStatus(
                BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]),
                packet[6], packet[7], packet[8] != 0);
            return true;
        }

        private static bool TryClassify(
            ushort type, ReadOnlySpan<byte> packet, out GameplayPeerDatagramKind kind)
        {
            kind = type switch
            {
                ApplicationReliablePushType => GameplayPeerDatagramKind.ApplicationReliablePush,
                ApplicationReliableAckType => GameplayPeerDatagramKind.ApplicationReliableAck,
                AddressUpdateType => GameplayPeerDatagramKind.AddressUpdate,
                TransportAckType => GameplayPeerDatagramKind.TransportAck,
                BadPingType => GameplayPeerDatagramKind.BadPingStatus,
                GeneralNpcCreateType or MasterGolemCreateType or MapNpcCreateType or
                EntityStateType or EntityEventType or MapNpcActionType or MapItemSnapshotType or
                GameplayTickType => GameplayPeerDatagramKind.ReliableMessage,
                _ => default
            };
            return type switch
            {
                ApplicationReliablePushType or ApplicationReliableAckType => packet.Length is 12 or 13,
                AddressUpdateType => packet.Length == 8,
                TransportAckType => packet.Length == 11,
                GeneralNpcCreateType or MasterGolemCreateType or MapNpcCreateType =>
                    TryParseNpcCreation(packet, out _),
                EntityStateType => TryParseEntityState(packet, out _),
                EntityEventType => packet.Length >= 19,
                MapNpcActionType => TryParseMapNpcCreateRequest(packet, out _),
                MapItemSnapshotType => TryParseMapItemSnapshot(packet, out _),
                BadPingType => packet.Length == 9 && packet[8] <= 1,
                GameplayTickType => packet.Length == 8,
                _ => false
            };
        }

        private static bool IsValidMapItemSnapshot(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < 8) return false;
            int count = packet[7];
            return count <= 0x41 && packet.Length == 8 + count * 2;
        }
    }
}
