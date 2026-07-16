using System.Net.Sockets;
using System.Threading.Tasks;
using RakionServer.World.Database;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class PresentStorageTests
    {
        [Fact]
        public async Task Accept_CommitsRequestedPhysicalCellInSession()
        {
            using var socket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var session = new ClientSession(socket, 1, null!);

            PresentAcceptResult result = await session.AcceptPresentIntoStorageAsync(
                37, available => Task.FromResult(available
                    ? new PresentAcceptResult(PresentAcceptStatus.Success, 91, 1040, 37, 3)
                    : new PresentAcceptResult(PresentAcceptStatus.SlotOccupied)));

            Assert.Equal(PresentAcceptStatus.Success, result.Status);
            Assert.Equal(1040, session.BoxItems[37]);
            Assert.Equal(91, session.BoxRowId[37]);
            Assert.Equal(3, session.BoxLevel[37]);
        }

        [Fact]
        public async Task Accept_DoesNotPersistOrReplaceOccupiedCell()
        {
            using var socket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var session = new ClientSession(socket, 1, null!);
            session.SetBoxCell(37, 1050, 2, 80);
            bool? availability = null;

            PresentAcceptResult result = await session.AcceptPresentIntoStorageAsync(
                37, available =>
                {
                    availability = available;
                    return Task.FromResult(new PresentAcceptResult(
                        PresentAcceptStatus.SlotOccupied));
                });

            Assert.False(availability);
            Assert.Equal(PresentAcceptStatus.SlotOccupied, result.Status);
            Assert.Equal(1050, session.BoxItems[37]);
            Assert.Equal(80, session.BoxRowId[37]);
        }
    }
}
