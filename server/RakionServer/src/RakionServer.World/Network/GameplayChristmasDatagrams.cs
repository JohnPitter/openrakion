using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    public readonly record struct GameplayChristmasSettingEvent(
        GameplayEntityEvent Envelope, byte Kind, GameplayVector3 Position);

    public readonly record struct GameplayChristmasNoticeEvent(
        GameplayEntityEvent Envelope, int MessageId);

    public readonly record struct GameplayEventItemCollectEvent(
        GameplayEntityEvent Envelope, int CollectorId, int Argument);

    public readonly record struct GameplayEntityReferenceEvent(
        GameplayEntityEvent Envelope, int EntityId);

    public readonly record struct GameplaySpawnChristmasBoxEvent(
        GameplayEntityEvent Envelope, GameplayVector3 Position, byte Kind, int Argument);

    public readonly record struct GameplayChristmasBoxActorEvent(
        GameplayEntityEvent Envelope, byte ActorId);

    public readonly record struct GameplaySpawnEventItemEvent(
        GameplayEntityEvent Envelope, int EntityId, int Argument, byte Kind, byte OwnerId);

    public static partial class GameplayPeerDatagramCodec
    {
        public const uint ChristmasDestroyEventId = 0x0191001d;
        public const uint ChristmasNoticeEventId = 0x0191001f;
        public const uint ChristmasSettingEventId = 0x01910020;
        public const uint EventItemSettingEventId = 0x01910021;
        public const uint GetEventItemEventId = 0x01910022;
        public const uint DestroyEventItemEventId = 0x01910023;
        public const uint SpawnChristmasBoxEventId = 0x52b30000;
        public const uint ChristmasBoxItemTouchEventId = 0x52b30001;
        public const uint ChristmasBoxReceiveEventId = 0x52b30002;
        public const uint SpawnEventItemEventId = 0x52b50000;

        public static bool TryParseChristmasSetting(
            ReadOnlySpan<byte> packet, out GameplayChristmasSettingEvent value) =>
            TryParseSetting(packet, ChristmasSettingEventId, out value);

        public static bool TryParseEventItemSetting(
            ReadOnlySpan<byte> packet, out GameplayChristmasSettingEvent value) =>
            TryParseSetting(packet, EventItemSettingEventId, out value);

        public static bool TryParseChristmasNotice(
            ReadOnlySpan<byte> packet, out GameplayChristmasNoticeEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, ChristmasNoticeEventId, 4, out var envelope))
                return false;
            value = new GameplayChristmasNoticeEvent(
                envelope, BinaryPrimitives.ReadInt32LittleEndian(packet[19..]));
            return true;
        }

        public static bool TryParseGetEventItem(
            ReadOnlySpan<byte> packet, out GameplayEventItemCollectEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, GetEventItemEventId, 8, out var envelope))
                return false;
            value = new GameplayEventItemCollectEvent(
                envelope,
                BinaryPrimitives.ReadInt32LittleEndian(packet[19..]),
                BinaryPrimitives.ReadInt32LittleEndian(packet[23..]));
            return true;
        }

        public static bool TryParseDestroyEventItem(
            ReadOnlySpan<byte> packet, out GameplayEntityReferenceEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, DestroyEventItemEventId, 4, out var envelope))
                return false;
            value = new GameplayEntityReferenceEvent(
                envelope, BinaryPrimitives.ReadInt32LittleEndian(packet[19..]));
            return true;
        }

        public static bool TryParseSpawnChristmasBox(
            ReadOnlySpan<byte> packet, out GameplaySpawnChristmasBoxEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, SpawnChristmasBoxEventId, 20, out var envelope))
                return false;
            value = new GameplaySpawnChristmasBoxEvent(
                envelope,
                ReadVector3(packet[19..]),
                packet[31],
                BinaryPrimitives.ReadInt32LittleEndian(packet[35..]));
            return true;
        }

        public static bool TryParseChristmasBoxItemTouch(
            ReadOnlySpan<byte> packet, out GameplayChristmasBoxActorEvent value) =>
            TryParseChristmasBoxActor(packet, ChristmasBoxItemTouchEventId, out value);

        public static bool TryParseChristmasBoxReceive(
            ReadOnlySpan<byte> packet, out GameplayChristmasBoxActorEvent value) =>
            TryParseChristmasBoxActor(packet, ChristmasBoxReceiveEventId, out value);

        public static bool TryParseSpawnEventItem(
            ReadOnlySpan<byte> packet, out GameplaySpawnEventItemEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, SpawnEventItemEventId, 12, out var envelope))
                return false;
            value = new GameplaySpawnEventItemEvent(
                envelope,
                BinaryPrimitives.ReadInt32LittleEndian(packet[19..]),
                BinaryPrimitives.ReadInt32LittleEndian(packet[23..]),
                packet[27], packet[28]);
            return true;
        }

        private static bool TryParseSetting(
            ReadOnlySpan<byte> packet,
            uint eventId,
            out GameplayChristmasSettingEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, eventId, 16, out var envelope)) return false;
            value = new GameplayChristmasSettingEvent(
                envelope, packet[19], ReadVector3(packet[23..]));
            return true;
        }

        private static bool TryParseChristmasBoxActor(
            ReadOnlySpan<byte> packet,
            uint eventId,
            out GameplayChristmasBoxActorEvent value)
        {
            value = default;
            if (!TryParseExpectedEvent(packet, eventId, 4, out var envelope)) return false;
            value = new GameplayChristmasBoxActorEvent(envelope, packet[19]);
            return true;
        }

        private static bool IsValidKnownChristmasEvent(
            ReadOnlySpan<byte> packet, GameplayEntityEvent envelope) =>
            envelope.EventId switch
            {
                ChristmasDestroyEventId => envelope.PayloadLength == 0,
                ChristmasNoticeEventId => TryParseChristmasNotice(packet, out _),
                ChristmasSettingEventId => TryParseChristmasSetting(packet, out _),
                EventItemSettingEventId => TryParseEventItemSetting(packet, out _),
                GetEventItemEventId => TryParseGetEventItem(packet, out _),
                DestroyEventItemEventId => TryParseDestroyEventItem(packet, out _),
                SpawnChristmasBoxEventId => TryParseSpawnChristmasBox(packet, out _),
                ChristmasBoxItemTouchEventId => TryParseChristmasBoxItemTouch(packet, out _),
                ChristmasBoxReceiveEventId => TryParseChristmasBoxReceive(packet, out _),
                SpawnEventItemEventId => TryParseSpawnEventItem(packet, out _),
                _ => true
            };
    }
}
