using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class GmFieldEntryProtocolTests
    {
        [Fact]
        public void RequestReadsFieldIdAndIgnoresTransportTail()
        {
            Assert.True(GmFieldEntryRequest.TryParse(
                new byte[] { 0x34, 0x12, 0xAA }, out var request));
            Assert.Equal((ushort)0x1234, request.FieldId);
            Assert.False(GmFieldEntryRequest.TryParse(new byte[] { 0x34 }, out _));
        }

        [Theory]
        [InlineData(GmFieldEntryStatus.OutOfRange, "0900013412")]
        [InlineData(GmFieldEntryStatus.Free, "0900023412")]
        public void NonSuccessResponseHasFixedFiveByteLayout(
            GmFieldEntryStatus status, string expected)
        {
            var snapshot = new GmFieldEntrySnapshot(status, 0x1234);

            Assert.Equal(expected, Hex(GmFieldEntryFrames.Response(snapshot)));
        }

        [Fact]
        public void SuccessResponseAppendsRoomThenCreatorCStrings()
        {
            var snapshot = new GmFieldEntrySnapshot(
                GmFieldEntryStatus.Success, 0x1234, "Room", "Creator");

            Assert.Equal("0900003412526f6f6d0043726561746f7200",
                Hex(GmFieldEntryFrames.Response(snapshot)));
        }

        [Fact]
        public void QueryDistinguishesActiveFreeAndOutOfRangeSlots()
        {
            var config = new WorldConfig { MaxField = 3 };
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            server.Fields.Add(new Field(1)
            {
                State = 1,
                Name = "Arena",
                CreatorCharacterName = "Master"
            });

            Assert.Equal(new GmFieldEntrySnapshot(
                    GmFieldEntryStatus.Success, 1, "Arena", "Master"),
                server.QueryGmFieldEntry(1));
            Assert.Equal(GmFieldEntryStatus.Free,
                server.QueryGmFieldEntry(2).Status);
            Assert.Equal(GmFieldEntryStatus.OutOfRange,
                server.QueryGmFieldEntry(3).Status);
        }

        private static string Hex(byte[] value) =>
            System.Convert.ToHexString(value).ToLowerInvariant();
    }
}
