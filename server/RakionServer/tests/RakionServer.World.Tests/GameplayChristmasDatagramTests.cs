using System;
using System.Buffers.Binary;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class GameplayChristmasDatagramTests
    {
        [Fact]
        public void Settings_DecodeKindAndPosition()
        {
            byte[] christmas = BuildEvent(
                GameplayPeerDatagramCodec.ChristmasSettingEventId,
                "020000000000803F0000004000004040");
            byte[] eventItem = BuildEvent(
                GameplayPeerDatagramCodec.EventItemSettingEventId,
                "03000000000080BF0000003F00001040");

            Assert.True(GameplayPeerDatagramCodec.TryParseChristmasSetting(
                christmas, out var christmasSetting));
            Assert.Equal((byte)2, christmasSetting.Kind);
            Assert.Equal(new GameplayVector3(1f, 2f, 3f), christmasSetting.Position);
            Assert.True(GameplayPeerDatagramCodec.TryParseEventItemSetting(
                eventItem, out var eventItemSetting));
            Assert.Equal((byte)3, eventItemSetting.Kind);
            Assert.Equal(new GameplayVector3(-1f, 0.5f, 2.25f), eventItemSetting.Position);
        }

        [Fact]
        public void PlayerEvents_DecodeNoticeCollectAndDestroy()
        {
            byte[] notice = BuildEvent(
                GameplayPeerDatagramCodec.ChristmasNoticeEventId, "07000000");
            byte[] collect = BuildEvent(
                GameplayPeerDatagramCodec.GetEventItemEventId, "4433221188776655");
            byte[] destroy = BuildEvent(
                GameplayPeerDatagramCodec.DestroyEventItemEventId, "FEFFFFFF");

            Assert.True(GameplayPeerDatagramCodec.TryParseChristmasNotice(notice, out var message));
            Assert.Equal(7, message.MessageId);
            Assert.True(GameplayPeerDatagramCodec.TryParseGetEventItem(collect, out var collected));
            Assert.Equal(0x11223344, collected.CollectorId);
            Assert.Equal(0x55667788, collected.Argument);
            Assert.True(GameplayPeerDatagramCodec.TryParseDestroyEventItem(destroy, out var removed));
            Assert.Equal(-2, removed.EntityId);
        }

        [Fact]
        public void ChristmasBoxEvents_DecodeExactNativePayloads()
        {
            byte[] spawn = BuildEvent(
                GameplayPeerDatagramCodec.SpawnChristmasBoxEventId,
                "0000803F000000400000404005AABBCC78563412");
            byte[] touch = BuildEvent(
                GameplayPeerDatagramCodec.ChristmasBoxItemTouchEventId, "09AABBCC");
            byte[] receive = BuildEvent(
                GameplayPeerDatagramCodec.ChristmasBoxReceiveEventId, "07AABBCC");

            Assert.True(GameplayPeerDatagramCodec.TryParseSpawnChristmasBox(spawn, out var box));
            Assert.Equal(new GameplayVector3(1f, 2f, 3f), box.Position);
            Assert.Equal((byte)5, box.Kind);
            Assert.Equal(0x12345678, box.Argument);
            Assert.True(GameplayPeerDatagramCodec.TryParseChristmasBoxItemTouch(touch, out var touched));
            Assert.Equal((byte)9, touched.ActorId);
            Assert.True(GameplayPeerDatagramCodec.TryParseChristmasBoxReceive(receive, out var received));
            Assert.Equal((byte)7, received.ActorId);
        }

        [Fact]
        public void SpawnEventItem_DecodesTwoWordsAndTwoBytes()
        {
            byte[] packet = BuildEvent(
                GameplayPeerDatagramCodec.SpawnEventItemEventId,
                "04030201080706050A0BCCDD");

            Assert.True(GameplayPeerDatagramCodec.TryParseSpawnEventItem(packet, out var item));
            Assert.Equal(0x01020304, item.EntityId);
            Assert.Equal(0x05060708, item.Argument);
            Assert.Equal((byte)10, item.Kind);
            Assert.Equal((byte)11, item.OwnerId);
        }

        [Theory]
        [InlineData(0x0191001d, "00")]
        [InlineData(0x01910020, "010000000000000000000000")]
        [InlineData(0x01910022, "00000000")]
        [InlineData(0x52b30000, "00000000000000000000000000000000")]
        [InlineData(0x52b50000, "0000000000000000")]
        public void RelayRejectsKnownChristmasEventWithWrongLength(uint eventId, string payload)
        {
            byte[] packet = BuildEvent(eventId, payload);

            Assert.False(GameplayPeerDatagramCodec.TryParse(packet, out _));
        }

        private static byte[] BuildEvent(uint eventId, string payloadHex)
        {
            byte[] payload = Convert.FromHexString(payloadHex);
            byte[] packet = new byte[19 + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(packet, 0x830c);
            packet[8] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11), eventId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(15), (uint)payload.Length);
            payload.CopyTo(packet, 19);
            return packet;
        }
    }
}
