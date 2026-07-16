using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldRoomInfoProtocolTests
    {
        [Fact]
        public void LineUsesChatSubtypeZeroSenderAndNoTerminator()
        {
            byte[] frame = FieldRoomInfoFrames.Line("ID[7] Status[1]");

            Assert.Equal("22000049445b375d205374617475735b315d", Hex(frame));
        }

        [Fact]
        public void ResponsesPreserveSixHeadersAndTwentySlotLines()
        {
            var slots = new FieldRoomInfoSlotSnapshot[0x14];
            slots[3] = new FieldRoomInfoSlotSnapshot(42, 4, 1, 2);
            var snapshot = new FieldRoomInfoSnapshot
            {
                Id = 7,
                Status = 2,
                CreatorCharacter = "Creator",
                Title = "Arena",
                Password = "pw",
                MinLevel = 10,
                MaxLevel = 20,
                Basic = 3,
                Map = 5,
                Mode = 1,
                Boss = 3,
                Tunneling = 1,
                OnVote = 1,
                VotePosition = 4,
                BanSlot = 8,
                Slots = slots
            };

            string[] lines = FieldRoomInfoFrames.Responses(snapshot)
                .Select(TextOf).ToArray();

            Assert.Equal(26, lines.Length);
            Assert.Equal("ID[7] Status[2]", lines[0]);
            Assert.Equal("Char[Creator] Title[Arena]", lines[1]);
            Assert.Equal("Password[pw]", lines[2]);
            Assert.Equal("Level[10~20] Basic[3] Map[5] Mode[1]", lines[3]);
            Assert.Equal("Boss[3] Tunneling[1]", lines[4]);
            Assert.Equal("OnVote[1] VotePos[4] BanSlot[8]", lines[5]);
            Assert.Equal("Slot[3] ID[42] Status[4] Auth[1] Vote[2]", lines[9]);
            Assert.Equal("Slot[19] ID[0] Status[0] Auth[0] Vote[0]", lines[25]);
        }

        [Fact]
        public void QueryReturnsZeroedValidSlotAndRejectsInvalidId()
        {
            var config = new WorldConfig { MaxField = 3 };
            var server = new WorldServer(config, new WorldDatabase(config.Db));

            Assert.True(server.TryGetRoomInfo(2, out var empty));
            Assert.Equal((ushort)2, empty.Id);
            Assert.All(empty.Slots, slot => Assert.Equal(default, slot));
            Assert.False(server.TryGetRoomInfo(-1, out _));
            Assert.False(server.TryGetRoomInfo(3, out _));
        }

        [Fact]
        public void QueryProjectsActiveFieldAndSlotIdentity()
        {
            var config = new WorldConfig { MaxField = 10 };
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            using var socket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var session = new ClientSession(socket, 42, server) { SubStatus = 1 };
            var field = new Field(7)
            {
                State = 2,
                Name = "Arena",
                CreatorCharacterName = "Creator",
                Password = "pw",
                MinLevel = 10,
                MaxLevel = 20,
                LevelRangeCode = 3,
                MapId = 5,
                Mode = 1,
                MasterSlot = 3,
                HasTunnelingClient = true
            };
            field.Slots[3].Session = session;
            field.Slots[3].State = 4;
            field.Slots[3].VoteState = 2;
            server.Fields.Add(field);

            Assert.True(server.TryGetRoomInfo(7, out var snapshot));
            Assert.Equal("Creator", snapshot.CreatorCharacter);
            Assert.Equal((byte)3, snapshot.Boss);
            Assert.Equal(1u, snapshot.Tunneling);
            Assert.Equal(new FieldRoomInfoSlotSnapshot(42, 4, 1, 2), snapshot.Slots[3]);
        }

        private static string TextOf(byte[] frame) =>
            Encoding.ASCII.GetString(frame, 3, frame.Length - 3);

        private static string Hex(byte[] value) =>
            Convert.ToHexString(value).ToLowerInvariant();
    }
}
