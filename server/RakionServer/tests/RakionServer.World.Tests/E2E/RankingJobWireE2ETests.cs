using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Ranking;
using RakionServer.World.CharSelect;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class RankingJobWireE2ETests
    {
        [Fact]
        public async Task JobPublishesSevenSnapshotsAndLoginCarriesCanonicalRanks()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var sandbox = await RankingE2ESandbox.CreateAsync(
                fixture.DbConnectionString);

            var repository = new RankingRepository(
                fixture.DbConnectionString, fixture.DbConnectionString);
            await new RankingJob(repository, activeMonths: 2).RunAsync(CancellationToken.None);

            RankingDatabaseState state = await sandbox.ReadStateAsync();
            AssertPublishedState(state);

            await using var client = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "ranking-login");
            client.Login("test2", "test2");
            byte[] login = client.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            ClientSession session = JourneyHelper.WaitForSession(fixture.Server!, "test2",
                value => value.LoginCharList != null);
            CharSummary summary = Assert.Single(
                session.LoginCharList!.Chars, value => value.CharacterId == 9001);
            AssertSummary(summary, state.Canonical);
            AssertWire(login, state.Canonical);
        }

        private static void AssertPublishedState(RankingDatabaseState state)
        {
            Assert.Equal(new RankingCharacterState(23, 1, 1), state.Canonical);
            Assert.Equal(new RankingSnapshotState(1, 23, 77), state.Total);
            Assert.Equal(new RankingSnapshotState(1, 23, 88), state.ByClass);
            Assert.Equal(7, state.SnapshotCounts.Count);
            Assert.True(state.SnapshotCounts["totalrankp"] >= 2);
            Assert.Equal(1, state.SnapshotCounts["archerrankp"]);
            Assert.Equal(0, state.TransientTableCount);
        }

        private static void AssertSummary(
            CharSummary summary, RankingCharacterState expected)
        {
            Assert.Equal(expected.Grade, summary.RankGrade);
            Assert.Equal(expected.TotalRank, summary.TotalRank);
            Assert.Equal(expected.ClassRank, summary.ClassRank);
            Assert.Equal((byte)0, summary.Auth);
        }

        private static void AssertWire(byte[] frame, RankingCharacterState expected)
        {
            byte[] name = Encoding.ASCII.GetBytes("ProbeTwo\0");
            int nameOffset = FindLast(frame, name);
            Assert.True(nameOffset >= 5, "registro ProbeTwo ausente no 0x0C");
            int fields = nameOffset + name.Length;
            Assert.Equal(unchecked((byte)expected.ClassRank), frame[fields + 12]);
            Assert.Equal(0u,
                BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(fields + 13)));
            Assert.Equal(expected.TotalRank,
                BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(fields + 17)));
            Assert.Equal(expected.Grade, frame[fields + 21]);
        }

        private static int FindLast(byte[] source, byte[] value)
        {
            for (int offset = source.Length - value.Length; offset >= 0; offset--)
                if (source.AsSpan(offset, value.Length).SequenceEqual(value)) return offset;
            return -1;
        }
    }
}
