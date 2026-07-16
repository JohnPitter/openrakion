using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryUiStateTests
    {
        [Fact]
        public void OpenAndCloseFollowOriginalZeroOneTwoStatuses()
        {
            var state = new InventoryUiState();

            Assert.Equal((byte)1, state.Close(false));
            Assert.Equal((byte)0, state.Open(false));
            Assert.Equal((byte)1, state.Open(false));
            Assert.Equal((byte)2, state.Close(true));
            Assert.Equal((byte)0, state.Close(false));
            Assert.Equal((byte)1, state.Close(false));
        }

        [Fact]
        public void MutationInProgressDoesNotOpenClosedInventory()
        {
            var state = new InventoryUiState();

            Assert.Equal((byte)2, state.Open(true));
            Assert.Equal((byte)1, state.Close(false));
        }

        [Fact]
        public void MutationStatusMatchesOriginalStackGate()
        {
            var state = new InventoryUiState();

            Assert.Equal((byte)1, state.MutationStatus(false));
            Assert.Equal((byte)0, state.Open(false));
            Assert.Equal((byte)0, state.MutationStatus(false));
            Assert.Equal((byte)2, state.MutationStatus(true));
        }
    }
}
