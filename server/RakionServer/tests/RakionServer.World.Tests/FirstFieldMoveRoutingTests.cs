using System.Net.Sockets;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FirstFieldMoveRoutingTests
    {
        [Fact]
        public void FirstMoveInitializesSessionButContinuesToCanonicalDispatcher()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            using var socket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var session = new ClientSession(socket, 1, server)
            {
                InField = true,
                FieldSecondary = true,
                Status = UserStatus.InField,
                GameInfoId = 100,
                ActiveCharId = 200,
                FieldId = 1,
                PendingRoomMode = 2
            };
            var field = new Field(1)
            {
                State = 2,
                Mode = 2
            };
            field.Add(session);
            int seat = field.AssignSeat(session);
            field.ArmMatch(0);
            session.FieldSeat = (byte)seat;
            session.FieldObjectIndex = (ushort)seat;
            server.Fields.Add(field);

            bool intercepted = session.TryHandleLobbyEntry(0x4B, new byte[72]);

            Assert.False(intercepted);
            Assert.Equal((byte)3, field.Slots[seat].State);

            session.BeginFieldGameRoundStart();

            Assert.Equal((byte)4, field.Slots[seat].State);
        }
    }
}
