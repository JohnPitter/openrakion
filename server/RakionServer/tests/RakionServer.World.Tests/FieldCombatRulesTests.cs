using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldCombatRulesTests
    {
        [Fact]
        public void Deathmatch_CreditsIndividualKillerWithoutEliminatingVictim()
        {
            Field field = CreatePlayingField(GameMode.Deathmatch);

            DeathReportResult result = field.ApplyReportedDeath(0, 10, cause: 0);

            Assert.True(result.Processed);
            Assert.Equal((byte)0, result.ScoreA);
            Assert.Equal((byte)1, result.ScoreB);
            Assert.Equal((byte)1, field.Slots[10].RoundScore);
            Assert.False(field.Slots[0].Dead);
            Assert.Equal(MatchPhase.Playing, field.Phase);
        }

        [Fact]
        public void Deathmatch_SuicideDecrementsVictimAndSpecialKillAddsTwo()
        {
            Field field = CreatePlayingField(GameMode.Deathmatch);
            field.Slots[0].RoundScore = 2;

            DeathReportResult suicide = field.ApplyReportedDeath(0, 10, cause: 1);
            DeathReportResult special = field.ApplyReportedDeath(0, 10, cause: 8);

            Assert.Equal((byte)1, suicide.ScoreA);
            Assert.Equal((byte)0, suicide.ScoreB);
            Assert.Equal((byte)2, special.ScoreB);
        }

        [Fact]
        public void Deathmatch_FragLimitEndsRoundWithoutTeamWinMutation()
        {
            Field field = CreatePlayingField(GameMode.Deathmatch);
            field.FragLimit = 2;

            field.ApplyReportedDeath(0, 10, cause: 8);

            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
            Assert.Equal((byte)1, field.RoundEndReason);
            Assert.Equal((byte)0, field.Wins0);
            Assert.Equal((byte)0, field.Wins1);
        }

        [Fact]
        public void TeamDeath_UsesTeamScoreAndOriginalLosingSideEncoding()
        {
            Field field = CreatePlayingField(GameMode.TeamDeath);
            field.FragLimit = 2;

            DeathReportResult result = field.ApplyReportedDeath(0, 10, cause: 8);

            Assert.Equal((byte)0, result.ScoreA);
            Assert.Equal((byte)2, result.ScoreB);
            Assert.Equal((byte)1, field.Wins1);
            Assert.Equal((byte)0, field.LosingSideWire);
            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
            Assert.False(field.Slots[0].Dead);
        }

        [Fact]
        public void Golem_EliminatedTeamEndsRoundAndKeepsWireScoresAsKillerSeat()
        {
            Field field = CreatePlayingField(GameMode.Golem);

            DeathReportResult result = field.ApplyReportedDeath(0, 10, cause: 0);

            Assert.True(field.Slots[0].Dead);
            Assert.Equal((byte)10, result.ScoreA);
            Assert.Equal((byte)10, result.ScoreB);
            Assert.Equal((byte)1, field.Wins1);
            Assert.Equal((byte)0, field.LosingSideWire);
        }

        [Fact]
        public void StageParty_EndsOnlyAfterEveryPlayerIsDead()
        {
            Field field = CreateStageParty();

            field.ApplyReportedDeath(0, 0, cause: 1);

            Assert.Equal(MatchPhase.Playing, field.Phase);
            Assert.True(field.Slots[0].Dead);
            Assert.False(field.Slots[1].Dead);

            field.ApplyReportedDeath(1, 1, cause: 1);

            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
            Assert.Equal((byte)1, field.RoundEndReason);
            Assert.Equal((byte)1, field.Wins1);
            Assert.Equal((byte)0, field.LosingSideWire);
        }

        [Fact]
        public void StageParty_ClearRequiresMasterAndAwardsTeamZero()
        {
            Field field = CreateStageParty();
            field.MasterSlot = 0;

            Assert.False(field.ApplyStageClear(1));
            Assert.True(field.ApplyStageClear(0));
            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
            Assert.Equal((byte)2, field.RoundEndReason);
            Assert.Equal((byte)1, field.Wins0);
            Assert.Equal((byte)1, field.LosingSideWire);
        }

        [Fact]
        public void Boss_LeaderDeathAwardsRoundToOpposingTeam()
        {
            Field field = CreatePlayingField(GameMode.Boss);
            field.LeaderSlotA = 0;

            field.ApplyReportedDeath(0, 10, cause: 0);

            Assert.Equal((byte)1, field.Wins1);
            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
        }

        [Fact]
        public void ObjectivePair_FirstZeroAwardsTeamOneWithExactWireState()
        {
            Field field = CreatePlayingField(GameMode.Golem);

            bool ended = field.ApplyObjectivePair(0, 75);

            Assert.True(ended);
            Assert.Equal((short)0, field.ObjectivePairA);
            Assert.Equal((short)75, field.ObjectivePairB);
            Assert.Equal((byte)2, field.RoundEndReason);
            Assert.Equal((byte)0, field.LosingSideWire);
            Assert.Equal((byte)1, field.Wins1);
            Assert.Equal((byte)1, field.Slots[10].CounterB);
            Assert.Equal(new byte[] { 2, 0, 0, 1 }, field.Build0x4a());
        }

        [Fact]
        public void ObjectivePair_SecondZeroAwardsTeamZeroWithExactWireState()
        {
            Field field = CreatePlayingField(GameMode.Golem);

            bool ended = field.ApplyObjectivePair(75, 0);

            Assert.True(ended);
            Assert.Equal((byte)2, field.RoundEndReason);
            Assert.Equal((byte)1, field.LosingSideWire);
            Assert.Equal((byte)1, field.Wins0);
            Assert.Equal((byte)1, field.Slots[0].CounterA);
            Assert.Equal(new byte[] { 2, 1, 1, 0 }, field.Build0x4a());
        }

        [Fact]
        public void ObjectivePair_NonZeroValuesAreStoredWithoutEndingRound()
        {
            Field field = CreatePlayingField(GameMode.Golem);

            bool ended = field.ApplyObjectivePair(70, 80);

            Assert.False(ended);
            Assert.Equal((short)70, field.ObjectivePairA);
            Assert.Equal((short)80, field.ObjectivePairB);
            Assert.Equal(MatchPhase.Playing, field.Phase);
        }

        [Fact]
        public void BossTarget_OnlyLeaderInPlayingBossFieldCanUpdateTargets()
        {
            Field field = CreatePlayingField(GameMode.Boss);
            field.LeaderSlotA = 0;
            field.LeaderSlotB = 10;

            Assert.True(field.ApplyBossTarget(0, 4, 321));
            Assert.True(field.ApplyBossTarget(10, 12, 654));
            Assert.False(field.ApplyBossTarget(1, 4, 999));
            Assert.Equal((ushort)321, field.BossTargetA);
            Assert.Equal((ushort)654, field.BossTargetB);
        }

        [Fact]
        public void BossTarget_IsRejectedOutsideBossMode()
        {
            Field field = CreatePlayingField(GameMode.TeamDeath);
            field.LeaderSlotA = 0;

            Assert.False(field.ApplyBossTarget(0, 4, 321));
            Assert.Equal((ushort)0, field.BossTargetA);
        }

        [Fact]
        public void DeathmatchTimeoutDoesNotMutateTeamWins()
        {
            Field field = CreatePlayingField(GameMode.Deathmatch);

            field.EndRound(1);

            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
            Assert.Equal((byte)1, field.RoundEndReason);
            Assert.Equal((byte)0, field.Wins0);
            Assert.Equal((byte)0, field.Wins1);
        }

        [Fact]
        public void TeamDeathDepartureEndsRoundWhenOpposingTeamBecomesEmpty()
        {
            Field field = CreatePlayingField(GameMode.TeamDeath);

            bool ended = field.ApplyPlayerDeparture(10);

            Assert.True(ended);
            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
            Assert.Equal((byte)1, field.Wins0);
            Assert.Equal((byte)1, field.LosingSideWire);
            Assert.Equal((byte)0, field.Slots[10].State);
        }

        [Fact]
        public void TeamDeathGiveUpEndsRoundButKeepsDepartingSeatInExitState()
        {
            Field field = CreatePlayingField(GameMode.TeamDeath);

            bool ended = field.OnPlayerExit(10, cause: 0);

            Assert.True(ended);
            Assert.True(field.Slots[10].Dead);
            Assert.Equal((byte)1, field.Slots[10].State);
            Assert.Equal((byte)1, field.Wins0);
            Assert.Equal((byte)1, field.LosingSideWire);
        }

        [Fact]
        public void BossLeaderDepartureAwardsRoundToOpposingTeam()
        {
            Field field = CreatePlayingField(GameMode.Boss);
            field.LeaderSlotA = 0;

            bool ended = field.ApplyPlayerDeparture(0);

            Assert.True(ended);
            Assert.Equal((byte)1, field.Wins1);
            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
        }

        [Fact]
        public void DeathmatchDepartureEndsRoundWithFewerThanTwoActivePlayers()
        {
            Field field = CreatePlayingField(GameMode.Deathmatch);

            bool ended = field.ApplyPlayerDeparture(10);

            Assert.True(ended);
            Assert.Equal(MatchPhase.RoundEnd, field.Phase);
            Assert.Equal((byte)1, field.RoundEndReason);
            Assert.Equal((byte)0, field.Wins0);
            Assert.Equal((byte)0, field.Wins1);
        }

        [Fact]
        public void TunnelingPresenceEnablesOnceAndSynchronizesLaterPlayers()
        {
            Field field = CreatePlayingField(GameMode.TeamDeath);
            TunnelingPresenceChange first = field.RegisterTunnelingPresence(0, true);
            TunnelingPresenceChange second = field.RegisterTunnelingPresence(10, true);

            Assert.Equal(TunnelingPresenceChange.Enabled, first);
            Assert.Equal(TunnelingPresenceChange.SyncEnabled, second);
            Assert.True(field.HasTunnelingClient);
        }

        [Fact]
        public void TunnelingPresenceDisablesOnlyAfterLastActiveClientLeaves()
        {
            Field field = CreatePlayingField(GameMode.TeamDeath);
            field.HasTunnelingClient = true;
            field.Slots[0].UsesTunneling = true;
            field.Slots[10].UsesTunneling = true;

            TunnelingPresenceChange first = field.UnregisterTunnelingPresence(0);
            field.Slots[0].State = 0;
            TunnelingPresenceChange last = field.UnregisterTunnelingPresence(10);

            Assert.Equal(TunnelingPresenceChange.None, first);
            Assert.Equal(TunnelingPresenceChange.Disabled, last);
            Assert.False(field.HasTunnelingClient);
        }

        private static Field CreatePlayingField(GameMode mode)
        {
            var field = new Field(7)
            {
                Mode = (byte)mode,
                State = 2,
                Phase = MatchPhase.Playing
            };
            field.Slots[0].State = 4;
            field.Slots[10].State = 4;
            return field;
        }

        private static Field CreateStageParty()
        {
            var field = new Field(7)
            {
                Mode = 0,
                State = 2,
                Phase = MatchPhase.Playing
            };
            field.Slots[0].State = 4;
            field.Slots[1].State = 4;
            return field;
        }
    }
}
