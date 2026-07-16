using System.Net.Sockets;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldForceChangeTeamHandlerTests
    {
        [Fact]
        public void ResponseBodiesMatchOriginalWire()
        {
            Assert.Equal(new byte[] { 0, 1, 10 },
                FieldForceChangeTeamFrames.Changed(1, 10));
            Assert.Equal(new byte[] { 2 },
                FieldForceChangeTeamFrames.Denied());
        }

        [Fact]
        public void CanonicalHandlerMovesTargetDuringPreSpawnWindow()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            using var masterSocket = NewSocket();
            using var targetSocket = NewSocket();
            var master = NewSession(masterSocket, 0, server, UserSubStatus.Special);
            var target = NewSession(targetSocket, 1, server, UserSubStatus.Normal);
            var field = new Field(1) { State = 1, Master = master, MasterSlot = 0 };
            field.Add(master);
            field.Add(target);
            Assert.Equal(0, field.AssignSeat(master));
            Assert.Equal(1, field.AssignSeat(target));
            field.Slots[1].State = 2;
            server.Fields.Add(field);

            byte[] payload = { 1 };
            WorldHandlers.Dispatch(new HandlerContext(
                server, master, 0x5B, new PacketReader(payload), payload));

            Assert.Null(field.Slots[1].Session);
            Assert.Same(target, field.Slots[10].Session);
            Assert.Equal((byte)10, target.FieldSeat);
        }

        private static Socket NewSocket() => new(
            AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        private static ClientSession NewSession(
            Socket socket, ushort slot, WorldServer server, byte subStatus) => new(socket, slot, server)
            {
                InField = true,
                FieldSecondary = true,
                Status = UserStatus.InField,
                SubStatus = subStatus,
                GameInfoId = 100 + slot,
                ActiveCharId = 200 + slot,
                FieldId = 1
            };
    }
}
