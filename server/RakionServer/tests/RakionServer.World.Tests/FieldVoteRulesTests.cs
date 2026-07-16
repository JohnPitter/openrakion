using System.Net.Sockets;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldVoteRulesTests
    {
        [Fact]
        public void Vote_RequiresMasterAndThreePlayingPlayers()
        {
            var setup = CreateField();

            FieldVoteTransition memberAttempt = setup.Field.ProcessVote(0, 2, 1, "AFK", 1000);
            Assert.Equal(FieldVoteStatus.MasterOnly, memberAttempt.Status);

            setup.Field.Slots[2].State = 3;
            FieldVoteTransition tooFew = setup.Field.ProcessVote(0, 0, 1, "AFK", 1000);
            Assert.Equal(FieldVoteStatus.NotEnoughPlayers, tooFew.Status);
        }

        [Fact]
        public void Vote_OpenExcludesTargetAndFinalizesWithThirtyMinutePenalty()
        {
            var setup = CreateField();

            FieldVoteTransition opened = setup.Field.ProcessVote(0, 0, 1, "AFK", 1000);
            FieldVoteTransition targetVote = setup.Field.ProcessVote(1, 1, 0, null, 1100);
            FieldVoteTransition decidingVote = setup.Field.ProcessVote(1, 2, 0, null, 1200);

            Assert.True(opened.Opened);
            Assert.Equal("AFK", setup.Field.VoteReason);
            Assert.Equal(FieldVoteStatus.TargetCannotVote, targetVote.Status);
            Assert.NotNull(decidingVote.Final);
            Assert.Equal(3, decidingVote.Final!.Eligible);
            Assert.Equal(2, decidingVote.Final.Yes);
            Assert.True(decidingVote.Final.PenaltyApplied);
            Assert.True(setup.Field.IsVotePenalized(setup.Target, 1200));
            Assert.False(setup.Field.IsVotePenalized(setup.Target, 1_801_201));
            Assert.All(setup.Field.Slots, record => Assert.Equal(0, record.VoteState));
        }

        [Fact]
        public void Vote_TimeoutFinalizesWithoutPenaltyWhenParticipationIsTooLow()
        {
            var setup = CreateField();
            setup.Field.ProcessVote(0, 0, 1, "AFK", 1000);

            FieldVoteFinal? result = setup.Field.TickVote(61_000);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Eligible);
            Assert.Equal(1, result.Yes);
            Assert.False(result.PenaltyApplied);
            Assert.False(setup.Field.VoteActive);
        }

        [Fact]
        public void Vote_TargetDepartureCancelsWithResultOneAndNoPenalty()
        {
            var setup = CreateField();
            setup.Field.ProcessVote(0, 0, 1, "AFK", 1000);

            FieldVoteFinal? result = setup.Field.CancelVoteForDeparture(1);

            Assert.NotNull(result);
            Assert.Equal((byte)1, result!.Result);
            Assert.Equal((byte)1, result.TargetSeat);
            Assert.False(result.PenaltyApplied);
            Assert.False(setup.Field.VoteActive);
            Assert.All(setup.Field.Slots, record => Assert.Equal(0, record.VoteState));
            Assert.False(setup.Field.IsVotePenalized(setup.Target, 1200));
        }

        private static VoteSetup CreateField()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var master = NewSession(1, "master", server);
            var target = NewSession(2, "target", server);
            var voter = NewSession(3, "voter", server);
            var field = new Field(4) { State = 2, MaxPlayers = 8, Master = master, MasterSlot = 0 };
            AddPlaying(field, master);
            AddPlaying(field, target);
            AddPlaying(field, voter);
            return new(field, target);
        }

        private static void AddPlaying(Field field, ClientSession session)
        {
            field.Add(session);
            int seat = field.AssignSeat(session);
            field.Slots[seat].State = 4;
        }

        private static ClientSession NewSession(ushort slot, string userId, WorldServer server)
        {
            var session = new ClientSession(
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), slot, server);
            session.UserId = userId;
            return session;
        }

        private sealed record VoteSetup(Field Field, ClientSession Target);
    }
}
