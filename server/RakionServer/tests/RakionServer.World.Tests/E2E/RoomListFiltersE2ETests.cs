using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class RoomListFiltersE2ETests
    {
        [Fact]
        public async Task RefreshAndEveryModeFilterFollowClientWireOrder()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            WorldServer server = fixture.Server!;
            await using var stageOwner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "room-list-owner");
            await using var viewer = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "room-list-viewer");

            stageOwner.Login("test", "test");
            stageOwner.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            stageOwner.SelectCharacter(1);
            JourneyHelper.WaitForSession(server, "test",
                session => session.ActiveCharId > 0 && session.Status == UserStatus.FieldLobby);
            viewer.Login("test2", "test2");
            viewer.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            viewer.SelectCharacter(9001);
            JourneyHelper.WaitForSession(server, "test2",
                session => session.ActiveCharId > 0 && session.Status == UserStatus.FieldLobby);

            stageOwner.CreateRoom(new HeadlessWorldClient.RoomSpec(
                "stage-filter", 1, 0, 1, 432, 0, 1, 99));
            JourneyHelper.WaitUntil(() => server.Fields.Exists(field => field.Mode == 0),
                "sala de stage não foi criada");

            var sockets = new List<Socket>();
            try
            {
                for (byte mode = 1; mode < 5; mode++)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    sockets.Add(socket);
                    var master = new ClientSession(socket, (ushort)(100 + mode), server)
                    {
                        CharLevel = 10
                    };
                    server.CreateField(new RoomCreationOptions
                    {
                        Name = $"mode-filter-{mode}",
                        MapId = 1,
                        Mode = mode,
                        MinLevel = 1,
                        MaxLevel = 99
                    }, master);
                }

                AssertSingleMode(Query(viewer, 1 << 0, availableOnly: false), 0);
                AssertSingleMode(Query(viewer, 1 << 0, availableOnly: false), 0);
                AssertEmptyRoomList(Query(viewer, 1 << 0, availableOnly: true));
                for (byte mode = 1; mode < 5; mode++)
                    AssertSingleMode(Query(viewer, 1 << mode, availableOnly: true), mode);
            }
            finally
            {
                foreach (Socket socket in sockets) socket.Dispose();
            }
        }

        private static byte[] Query(
            HeadlessWorldClient client, int modeMask, bool availableOnly)
        {
            client.DrainReceived();
            client.RequestRoomList((byte)modeMask, availableOnly);
            return client.WaitForNext(frame =>
                frame.Length >= 3 && frame[0] == 0x36 && frame[1] == 0,
                JourneyHelper.Timeout);
        }

        private static void AssertSingleMode(byte[] frame, byte mode)
        {
            Assert.True(frame.Length >= 9);
            Assert.Equal((byte)1, frame[2]);
            Assert.Equal(mode, frame[8]);
        }

        private static void AssertEmptyRoomList(byte[] frame) =>
            Assert.Equal((byte)0, frame[2]);
    }
}
