using System;
using System.Net.Sockets;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class RoomRosterFrameGoldenTests
    {
        [Fact]
        public void SnapshotBody_EmptyRoom_HasOriginalVariableSlotLayout()
        {
            var field = new Field(7)
            {
                State = 1,
                MasterSlot = 0,
                MapId = 3,
                Mode = 2,
                MinLevel = 5,
                MaxLevel = 40,
                Round = 1,
                MaxRounds = 3,
                RoundDurationSec = 432,
                FragLimit = 10,
                Name = "Sala",
                Password = "pw",
                Description = "desc"
            };

            byte[] body = RoomRosterFrames.SnapshotBody(field);

            Assert.Equal("0700010003020528000103b0010a53616c61007077006465736300",
                Convert.ToHexString(body[..27]).ToLowerInvariant());
            Assert.Equal(47, body.Length);
            Assert.All(body[27..], value => Assert.Equal(0, value));
        }

        [Fact]
        public void PlayerJoined_EmptySlot_ContainsOnlySlotHeader()
        {
            var record = new PlayerRec { Slot = 10, State = 5 };

            Assert.Equal(new byte[] { 0x38, 0, 0, 10, 5 },
                RoomRosterFrames.PlayerJoined(record));
        }

        [Fact]
        public void PlayerJoined_TunnelingClient_SerializesOriginalRouteFlag()
        {
            using var socket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var session = new ClientSession(socket, 42, null!)
            {
                CharName = "A",
                BuddyName = "B"
            };
            var record = new PlayerRec
            {
                Slot = 4,
                State = 3,
                Session = session,
                UsesTunneling = true
            };

            byte[] body = RoomRosterFrames.PlayerJoined(record);

            Assert.Equal("38000004032A00004100420001",
                Convert.ToHexString(body[..13]));
        }

        [Fact]
        public void PlayerJoined_Bot_UsesCompatibilityEndpointMarker()
        {
            var bot = new BotPlayer { Name = "Rok", CharClass = 2, Level = 40 };
            var record = new PlayerRec { Slot = 10, State = 3, Bot = bot };

            string hex = Convert.ToHexString(RoomRosterFrames.PlayerJoined(record));

            Assert.Contains("049F7F000001049F", hex);
        }
    }
}
