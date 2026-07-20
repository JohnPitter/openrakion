using System;
using System.IO;
using RakionServer.Common;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class ServerStatusSnapshotStoreTests : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(), "rakion-status-test-" + Guid.NewGuid().ToString("N") + ".json");

        [Fact]
        public void RoundTripsFreshSnapshot()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var expected = new ServerStatusSnapshot(true, 7, 500, now);

            ServerStatusSnapshotStore.Write(expected, _path);
            ServerStatusSnapshot? actual = ServerStatusSnapshotStore.ReadFresh(
                TimeSpan.FromSeconds(6), now, _path);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void RejectsStaleSnapshot()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ServerStatusSnapshotStore.Write(
                new ServerStatusSnapshot(true, 7, 500, now.AddSeconds(-7)), _path);

            Assert.Null(ServerStatusSnapshotStore.ReadFresh(
                TimeSpan.FromSeconds(6), now, _path));
        }

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
