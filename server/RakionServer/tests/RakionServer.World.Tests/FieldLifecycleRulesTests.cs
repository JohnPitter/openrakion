using System.Net.Sockets;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldLifecycleRulesTests
    {
        [Fact]
        public void ArmMatch_CreatesIdentityAndFortySecondEngageDeadline()
        {
            Field field = NewField(GameMode.Golem);
            field.Slots[0].State = 1;

            field.ArmMatch(1000);

            Assert.NotEqual(System.Guid.Empty, field.MatchId);
            Assert.Equal(MatchPhase.Pre, field.Phase);
            Assert.Equal(41000, field.DeadlineMs);
            Assert.Equal((byte)3, field.Slots[0].State);
            Assert.False(field.Settled);
        }

        [Fact]
        public void EngageTimeout_StartsWithSpawnedPlayerAndDoesNotPromoteUnreadyPlayer()
        {
            Field field = NewField(GameMode.Golem);
            field.State = 2;
            field.Phase = MatchPhase.Pre;
            field.DeadlineMs = 100;
            field.Slots[0].State = 4;
            field.Slots[10].State = 3;

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(MatchLifecycleEvent.EngageStarted, transition.Event);
            Assert.Equal(MatchPhase.Playing, field.Phase);
            Assert.Equal((byte)3, field.Slots[10].State);
            Assert.Equal(100 + (field.RoundDurationSec + 3) * 1000L, field.DeadlineMs);
        }

        [Fact]
        public void SoloEngageTimeout_WithoutSpawnedPlayerEndsWithReasonOne()
        {
            Field field = NewSoloField();
            field.Slots[0].State = 3;

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(new(MatchLifecycleEvent.MatchEnded, 1), transition);
            Assert.Equal((byte)1, field.State);
            Assert.Equal((byte)1, field.Slots[0].State);
        }

        [Theory]
        [InlineData(GameMode.Golem)]
        [InlineData(GameMode.TeamDeath)]
        [InlineData(GameMode.Boss)]
        public void TeamModeTimeout_UsesModeSpecificValuesAndReasonZero(GameMode mode)
        {
            Field field = PlayingField(mode);
            if (mode == GameMode.Golem) { field.ObjectivePairA = 9; field.ObjectivePairB = 4; }
            if (mode == GameMode.TeamDeath) { field.Score0 = 9; field.Score1 = 4; }
            if (mode == GameMode.Boss) { field.BossTargetA = 9; field.BossTargetB = 4; }

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(MatchLifecycleEvent.RoundTimedOut, transition.Event);
            Assert.Equal((byte)0, field.RoundEndReason);
            Assert.Equal((byte)1, field.Wins0);
            Assert.Equal((byte)0, field.Wins1);
            Assert.Equal((byte)1, field.LosingSideWire);
            Assert.Equal(15100, field.DeadlineMs);
        }

        [Fact]
        public void DeathmatchTimeout_DoesNotMutateTeamWins()
        {
            Field field = PlayingField(GameMode.Deathmatch);
            field.Wins0 = 3;
            field.Wins1 = 2;

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(MatchLifecycleEvent.RoundTimedOut, transition.Event);
            Assert.Equal((byte)0, field.RoundEndReason);
            Assert.Equal((byte)3, field.Wins0);
            Assert.Equal((byte)2, field.Wins1);
        }

        [Fact]
        public void RoundEnd_AfterLastRoundEndsWithReasonTwo()
        {
            Field field = RoundEndField(GameMode.Golem);
            field.MaxRounds = 1;

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(new(MatchLifecycleEvent.MatchEnded, 2), transition);
        }

        [Theory]
        [InlineData(GameMode.Golem)]
        [InlineData(GameMode.TeamDeath)]
        [InlineData(GameMode.Boss)]
        public void TeamModeNextRound_RequiresAnActivePlayerOnEachSide(GameMode mode)
        {
            Field field = RoundEndField(mode);
            field.MaxRounds = 3;
            field.Slots[0].State = 4;
            field.Slots[10].State = 0;

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(new(MatchLifecycleEvent.MatchEnded, 5), transition);
        }

        [Fact]
        public void DeathmatchNextRound_RequiresTwoActivePlayers()
        {
            Field field = RoundEndField(GameMode.Deathmatch);
            field.MaxRounds = 3;
            field.Slots[0].State = 4;
            field.Slots[10].State = 0;

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(new(MatchLifecycleEvent.MatchEnded, 6), transition);
        }

        [Fact]
        public void ValidNextRound_ResetsOriginalObjectivesToOne()
        {
            Field field = RoundEndField(GameMode.Golem);
            field.MaxRounds = 3;
            field.Slots[0].State = 4;
            field.Slots[10].State = 3;
            field.ObjectivePairA = 20;
            field.ObjectivePairB = 30;

            MatchLifecycleTransition transition = field.AdvanceLifecycle(100);

            Assert.Equal(MatchLifecycleEvent.NextRoundStarted, transition.Event);
            Assert.Equal((byte)2, field.Round);
            Assert.Equal((short)1, field.ObjectivePairA);
            Assert.Equal((short)1, field.ObjectivePairB);
            Assert.Equal((ushort)1, field.BossTargetA);
            Assert.Equal((ushort)1, field.BossTargetB);
            Assert.Equal((byte)3, field.Slots[10].State);
        }

        [Fact]
        public void DuplicateReady_FromPlayingPlayer_IsIgnoredWithoutChangingState()
        {
            Field field = PlayingField(GameMode.Deathmatch);
            ClientSession session = AddSession(field, 1);
            PlayerRec record = field.FindRec(session)!;
            record.State = 4;
            record.Dead = true;

            PlayerReadyTransition transition = field.OnPlayerReady(session, 1000);

            Assert.Equal(PlayerReadyTransition.Ignored, transition);
            Assert.Equal((byte)4, record.State);
            Assert.True(record.Dead);
        }

        [Fact]
        public void Ready_DuringPlaying_JoinsCurrentRound()
        {
            Field field = PlayingField(GameMode.Deathmatch);
            ClientSession session = AddSession(field, 1);
            PlayerRec record = field.FindRec(session)!;
            record.State = 3;
            record.Dead = true;

            PlayerReadyTransition transition = field.OnPlayerReady(session, 1000);

            Assert.Equal(PlayerReadyTransition.JoinedPlaying, transition);
            Assert.Equal((byte)4, record.State);
            Assert.False(record.Dead);
        }

        [Fact]
        public void Ready_DuringRoundEnd_SynchronizesResultAndCounter()
        {
            Field field = RoundEndField(GameMode.TeamDeath);
            field.LosingSideWire = 1;
            ClientSession session = AddSession(field, 1);
            PlayerRec record = field.FindRec(session)!;
            record.State = 3;
            record.CounterB = 2;

            PlayerReadyTransition transition = field.OnPlayerReady(session, 1000);

            Assert.Equal(PlayerReadyTransition.JoinedRoundEnd, transition);
            Assert.Equal((byte)4, record.State);
            Assert.Equal((byte)3, record.CounterB);
        }

        private static Field NewField(GameMode mode) => new(7) { Mode = (byte)mode };

        private static ClientSession AddSession(Field field, ushort slot)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var session = new ClientSession(socket, slot, null!);
            field.Add(session);
            field.AssignSeat(session);
            return session;
        }

        private static Field NewSoloField() => new(7)
        {
            Mode = 0,
            State = 2,
            Phase = MatchPhase.Pre,
            DeadlineMs = 100
        };

        private static Field PlayingField(GameMode mode)
        {
            Field field = NewField(mode);
            field.State = 2;
            field.Phase = MatchPhase.Playing;
            field.Round = 1;
            field.DeadlineMs = 100;
            field.Slots[0].State = 4;
            field.Slots[10].State = 4;
            return field;
        }

        private static Field RoundEndField(GameMode mode)
        {
            Field field = PlayingField(mode);
            field.Phase = MatchPhase.RoundEnd;
            return field;
        }
    }
}
