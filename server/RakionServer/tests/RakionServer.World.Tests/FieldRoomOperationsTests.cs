using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldRoomOperationsTests
    {
        [Fact]
        public void AssignSeat_HumanStartsInWaitRoomInsteadOfPlaying()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var session = NewSession(1, server);
            var field = new Field(3) { State = 1, MaxPlayers = 8 };
            field.Add(session);

            int seat = field.AssignSeat(session);

            Assert.Equal(0, seat);
            Assert.Equal((byte)1, field.Slots[seat].State);
            Assert.False(field.Slots[seat].LobbyReady);
            Assert.False(field.Slots[seat].Playing);
        }

        [Fact]
        public void ChangeTeam_MovesRecordAndUpdatesSessionSeat()
        {
            var (field, master, member) = CreateRoom();

            Assert.True(field.TryChangeTeam(member, out byte oldSeat, out byte newSeat));

            Assert.Equal(1, oldSeat);
            Assert.Equal(10, newSeat);
            Assert.Null(field.Slots[oldSeat].Session);
            Assert.Same(member, field.Slots[newSeat].Session);
            Assert.Equal(newSeat, member.FieldSeat);
            Assert.Same(master, field.Master);
        }

        [Fact]
        public void ChangeTeam_UsesEntireOppositeTenSeatBlock()
        {
            var (field, _, member) = CreateRoom();
            for (int seat = 10; seat <= 17; seat++) field.Slots[seat].State = 5;

            Assert.True(field.TryChangeTeam(member, out byte oldSeat, out byte newSeat));

            Assert.Equal((byte)1, oldSeat);
            Assert.Equal((byte)18, newSeat);
            Assert.Same(member, field.Slots[18].Session);
        }

        [Fact]
        public void ChangeTeam_CanReturnThroughSeatEightWhenEarlierSeatsAreUnavailable()
        {
            var (field, _, member) = CreateRoom();
            Assert.True(field.TryChangeTeam(member, out _, out _));
            for (int seat = 1; seat <= 7; seat++) field.Slots[seat].State = 5;

            Assert.True(field.TryChangeTeam(member, out byte oldSeat, out byte newSeat));

            Assert.Equal((byte)10, oldSeat);
            Assert.Equal((byte)8, newSeat);
            Assert.Same(member, field.Slots[8].Session);
        }

        [Fact]
        public void BossLeadersUseHighestCharacterLevelAndPreserveFirstSeatOnTie()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var field = new Field(3) { Mode = (byte)GameMode.Boss };
            ClientSession a0 = NewSession(1, server);
            ClientSession a1 = NewSession(2, server);
            ClientSession b0 = NewSession(3, server);
            ClientSession b1 = NewSession(4, server);
            a0.CharLevel = 20;
            a1.CharLevel = 20;
            b0.CharLevel = 10;
            b1.CharLevel = 30;
            SetPlaying(field, 0, a0);
            SetPlaying(field, 1, a1);
            SetPlaying(field, 10, b0);
            SetPlaying(field, 11, b1);

            field.StartRound(1000);

            Assert.Equal((byte)0, field.LeaderSlotA);
            Assert.Equal((byte)11, field.LeaderSlotB);
        }

        [Fact]
        public void PlayerExitKeepsMasterWhilePlayerRemainsInRoom()
        {
            var (field, master, member) = CreateRoom();
            field.State = 2;
            field.Phase = MatchPhase.Playing;
            field.Slots[0].State = 4;
            field.Slots[1].State = 4;

            field.OnPlayerExit(0, cause: 0);

            Assert.Same(master, field.Master);
            Assert.NotSame(member, field.Master);
            Assert.Equal(0, field.MasterSlot);
        }

        [Fact]
        public void RotateMaster_TransfersAuthorityDeterministically()
        {
            var (field, master, member) = CreateRoom();

            Assert.True(field.TryRotateMaster(master, out byte oldSeat, out byte newSeat));

            Assert.Equal(0, oldSeat);
            Assert.Equal(1, newSeat);
            Assert.Same(member, field.Master);
            Assert.Equal(UserSubStatus.Normal, master.SubStatus);
            Assert.Equal(UserSubStatus.Normal, member.SubStatus);
        }

        [Fact]
        public void SlotUdpRelayTargetsRequestedSeatAndPublishesSenderSeat()
        {
            var (field, master, member) = CreateRoom();

            Assert.True(field.TryResolveSlotUdpRelay(
                master, 1, out ClientSession? target, out byte senderSeat));
            Assert.Same(member, target);
            Assert.Equal((byte)0, senderSeat);
            Assert.False(field.TryResolveSlotUdpRelay(
                master, 8, out _, out _));
        }

        [Fact]
        public void SlotLock_RequiresMasterAndEmptyUsableSeat()
        {
            var (field, master, member) = CreateRoom();

            Assert.False(field.TrySetSlotLock(member, 2, true));
            Assert.False(field.TrySetSlotLock(master, 8, true));
            Assert.True(field.TrySetSlotLock(master, 2, true));
            Assert.Equal(5, field.Slots[2].State);
            Assert.True(field.TrySetSlotLock(master, 2, false));
            Assert.Equal(0, field.Slots[2].State);
        }

        [Fact]
        public void ForceChangeTeam_MovesRequestedRecordToFirstOppositeSeat()
        {
            var (field, _, member) = CreateRoom();
            field.Slots[1].State = 1;
            field.Slots[1].RoundScore = 7;

            ForcedTeamChangeResult result = field.ForceChangeTeam(1, out byte newSeat);

            Assert.Equal(ForcedTeamChangeResult.Changed, result);
            Assert.Equal(10, newSeat);
            Assert.Null(field.Slots[1].Session);
            Assert.Same(member, field.Slots[10].Session);
            Assert.Equal(7, field.Slots[10].RoundScore);
            Assert.Equal(10, member.FieldSeat);
        }

        [Fact]
        public void ForceChangeTeam_MovesReadyTargetAndPreservesReadyState()
        {
            var (field, _, member) = CreateRoom();
            field.Slots[1].State = 2;

            ForcedTeamChangeResult result = field.ForceChangeTeam(1, out byte newSeat);

            Assert.Equal(ForcedTeamChangeResult.Changed, result);
            Assert.Equal(10, newSeat);
            Assert.Null(field.Slots[1].Session);
            Assert.Same(member, field.Slots[10].Session);
            Assert.Equal((byte)2, field.Slots[10].State);
        }

        [Fact]
        public void StageRemoval_NormalMemberCannotBypassMasterGate()
        {
            var (server, field, master, member) = CreateServerRoom();

            bool removed = server.TryRemoveFieldMember(
                member, 0, out ClientSession? victim, out bool unauthorized);

            Assert.False(removed);
            Assert.True(unauthorized);
            Assert.Null(victim);
            Assert.Same(master, field.Slots[0].Session);
        }

        [Fact]
        public void StageRemoval_SpecialTargetIsProtected()
        {
            var (server, field, master, member) = CreateServerRoom();
            member.SubStatus = UserSubStatus.Special;

            bool removed = server.TryRemoveFieldMember(
                master, 1, out ClientSession? victim, out bool unauthorized);

            Assert.False(removed);
            Assert.False(unauthorized);
            Assert.Null(victim);
            Assert.Same(member, field.Slots[1].Session);
        }

        [Fact]
        public void Kick_ClearsVictimFieldIdentityWithoutOrphanSeat()
        {
            var (server, _, master, member) = CreateServerRoom();

            Assert.True(server.TryKickFieldMember(master, 1, out ClientSession? victim));

            Assert.Same(member, victim);
            Assert.Equal(-1, member.FieldId);
            Assert.Equal(Field.NoSeat, member.FieldSeat);
            Assert.Equal((ushort)Field.NoSeat, member.FieldObjectIndex);
        }

        [Fact]
        public void Close_ClearsEveryMemberFieldIdentity()
        {
            var (server, _, master, member) = CreateServerRoom();

            Assert.True(server.TryCloseField(master, out ClientSession[] members));

            Assert.Equal(2, members.Length);
            Assert.All(members, session =>
            {
                Assert.Equal(-1, session.FieldId);
                Assert.Equal(Field.NoSeat, session.FieldSeat);
                Assert.Equal((ushort)Field.NoSeat, session.FieldObjectIndex);
            });
        }

        [Fact]
        public void RoomListQuery_AppliesCursorDirectionModeAndEligibility()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var firstMaster = NewSession(1, server);
            var secondMaster = NewSession(2, server);
            var viewer = NewSession(3, server);
            viewer.CharLevel = 10;
            Field first = server.CreateField(RoomOptions("mode1", 1), firstMaster);
            Field second = server.CreateField(RoomOptions("mode2", 2), secondMaster);

            Assert.True(first.Settled);
            first.ResetMatch();
            Assert.False(first.Settled);
            Guid matchId = first.MatchId;
            Assert.NotEqual(Guid.Empty, matchId);
            first.ResetMatch();
            Assert.NotEqual(matchId, first.MatchId);
            first.MinLevel = second.MinLevel = 1;
            first.MaxLevel = second.MaxLevel = 99;

            var forward = new RoomListQuery(10, 0, true, 1 << 2, false);
            var backward = new RoomListQuery(10, (ushort)(second.Id + 1), false, 1 << 2, false);

            Assert.Equal(1, first.Id);
            Assert.Equal(new[] { second.Id },
                server.ListJoinableFields(viewer, forward).Select(field => (int)field.FieldId));
            Assert.Equal(new[] { second.Id },
                server.ListJoinableFields(viewer, backward).Select(field => (int)field.FieldId));
        }

        [Fact]
        public void RoomListQuery_AppliesEveryModeFilterAndRefreshIsStable()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var viewer = NewSession(20, server);
            viewer.CharLevel = 10;
            var fields = new Field[5];
            for (byte mode = 0; mode < fields.Length; mode++)
                fields[mode] = server.CreateField(
                    RoomOptions($"mode{mode}", mode), NewSession((ushort)(mode + 1), server));

            for (byte mode = 0; mode < fields.Length; mode++)
            {
                var query = new RoomListQuery(10, 0, true, (byte)(1 << mode), false);
                int[] first = server.ListJoinableFields(viewer, query)
                    .Select(room => (int)room.FieldId).ToArray();
                int[] refreshed = server.ListJoinableFields(viewer, query)
                    .Select(room => (int)room.FieldId).ToArray();
                Assert.Equal(new[] { fields[mode].Id }, first);
                Assert.Equal(first, refreshed);
            }
        }

        [Fact]
        public void AvailableFilterKeepsPlayingBattleButHidesUnavailableStage()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var viewer = NewSession(20, server);
            viewer.CharLevel = 10;
            Field available = server.CreateField(RoomOptions("available", 1), NewSession(1, server));
            Field full = server.CreateField(
                RoomOptions("full", 1) with { CapacityOverride = 1 }, NewSession(2, server));
            Field playing = server.CreateField(RoomOptions("playing", 1), NewSession(3, server));
            Field ineligible = server.CreateField(
                RoomOptions("ineligible", 1) with { MinLevel = 20 }, NewSession(4, server));
            Field playingStage = server.CreateField(
                RoomOptions("playing-stage", 0), NewSession(5, server));
            playing.State = 2;
            playingStage.State = 2;

            var availableOnly = new RoomListQuery(10, 0, true, 0x1f, false);
            var allStatuses = availableOnly with { IncludeUnavailable = true };

            Assert.Equal(new[] { available.Id, playing.Id },
                server.ListJoinableFields(viewer, availableOnly).Select(room => (int)room.FieldId));
            Assert.Equal(new[]
                { available.Id, full.Id, playing.Id, ineligible.Id, playingStage.Id },
                server.ListJoinableFields(viewer, allStatuses).Select(room => (int)room.FieldId));
        }

        [Fact]
        public void MatchEnd_MakesRoomAvailableAndBotReadyForRematch()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var master = NewSession(1, server);
            var viewer = NewSession(2, server);
            master.CharLevel = viewer.CharLevel = 10;
            Field field = server.CreateField(
                RoomOptions("rematch", (byte)GameMode.Deathmatch), master);
            var bot = new BotPlayer { Name = "Rok", Team = 1 };
            bot.InitHealth(10);
            int botSeat = field.AddBot(bot, 1);
            field.State = 2;
            field.Slots[0].State = 4;
            field.Slots[botSeat].State = 4;
            Assert.True(bot.TakeDamage(bot.MaxHealth));

            field.EndMatch(0);

            Assert.Equal((byte)1, field.State);
            Assert.Equal((byte)1, field.Slots[0].State);
            Assert.Equal((byte)2, field.Slots[botSeat].State);
            Assert.True(bot.Alive);
            var query = new RoomListQuery(10, 0, true, 1 << 2, false);
            Assert.Contains(server.ListJoinableFields(viewer, query), room => room.FieldId == field.Id);
        }

        [Fact]
        public void CompetitiveCreation_UsesOriginalTwelveSeatsAndPublishesCompleteSnapshot()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var master = NewSession(1, server);
            var options = RoomOptions("golden", (byte)GameMode.Golem) with
            {
                Password = "pw",
                Rounds = 3,
                DurationSeconds = 432,
                FragLimit = 7,
                MinLevel = 5,
                MaxLevel = 40,
                LevelRangeCode = 9
            };

            Field field = server.CreateField(options, master);
            RoomListSnapshot snapshot = field.CaptureRoomListSnapshot();

            Assert.Equal(12, field.MaxPlayers);
            Assert.True(snapshot.HasPassword);
            Assert.Equal((byte)5, snapshot.MinLevel);
            Assert.Equal((byte)40, snapshot.MaxLevel);
            Assert.Equal((byte)9, snapshot.LevelRangeCode);
            Assert.Equal((byte)3, snapshot.MaxRounds);
            Assert.Equal((byte)1, snapshot.PlayerCount);
            Assert.Equal((byte)12, snapshot.MaxPlayers);
            Assert.All(new[] { 0, 1, 2, 3, 4, 5, 10, 11, 12, 13, 14, 15 },
                seat => Assert.NotEqual((byte)5, field.Slots[seat].State));
            Assert.All(new[] { 6, 7, 8, 9, 16, 17, 18, 19 },
                seat => Assert.Equal((byte)5, field.Slots[seat].State));
        }

        [Fact]
        public void ExplicitlyHiddenField_IsExcludedFromPublicRoomList()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            Field field = server.CreateField(new RoomCreationOptions
            {
                Name = "stage",
                MapId = 3,
                Searchable = false
            }, NewSession(1, server));

            Assert.Same(field, server.GetField(field.Id));
            Assert.Empty(server.ListJoinableFields(0, 10));
        }

        [Fact]
        public void FieldAllocation_RespectsLimitAndReusesReleasedId()
        {
            var config = new WorldConfig { MaxField = 3 };
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var firstMaster = NewSession(1, server);
            var secondMaster = NewSession(2, server);
            Field first = server.CreateField(RoomOptions("first", 1), firstMaster);
            Field second = server.CreateField(RoomOptions("second", 1), secondMaster);

            Assert.Equal(1, first.Id);
            Assert.Equal(2, second.Id);
            Assert.Throws<InvalidOperationException>(() =>
                server.CreateField(RoomOptions("full", 1), NewSession(3, server)));
            Assert.True(server.TryCloseField(firstMaster, out _));

            Field reused = server.CreateField(RoomOptions("reused", 1), NewSession(4, server));
            Assert.Equal(1, reused.Id);
        }

        [Fact]
        public async Task ConcurrentJoins_NeverExceedCapacityOrDuplicateSeats()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var master = NewSession(1, server);
            Field field = server.CreateField(RoomOptions("stress", 1), master);
            ClientSession[] candidates = Enumerable.Range(2, 40)
                .Select(slot => NewSession((ushort)slot, server))
                .ToArray();

            await Task.WhenAll(candidates.Select(session =>
                Task.Run(() => server.JoinField(session, field, false))));

            ClientSession[] members;
            lock (field.SyncRoot) members = field.Players.ToArray();
            Assert.Equal(field.MaxPlayers, members.Length);
            Assert.Equal(members.Length, members.Select(member => member.FieldSeat).Distinct().Count());
            Assert.All(members, member => Assert.InRange(member.FieldSeat, (byte)0, (byte)19));
        }

        [Fact]
        public async Task ConcurrentCreations_AllocateUniqueBoundedIds()
        {
            var config = new WorldConfig { MaxField = 64 };
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            Task<Field>[] creations = Enumerable.Range(1, 40)
                .Select(slot => Task.Run(() => server.CreateField(
                    RoomOptions($"room-{slot}", 1), NewSession((ushort)slot, server))))
                .ToArray();

            Field[] fields = await Task.WhenAll(creations);

            Assert.Equal(fields.Length, fields.Select(field => field.Id).Distinct().Count());
            Assert.All(fields, field => Assert.InRange(field.Id, 1, config.MaxField - 1));
        }

        [Fact]
        public async Task ConcurrentJoinCloseAndList_LeavesNoOrphanMembership()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var master = NewSession(1, server);
            Field field = server.CreateField(RoomOptions("close-race", 1), master);
            ClientSession[] candidates = Enumerable.Range(2, 30)
                .Select(slot => NewSession((ushort)slot, server))
                .ToArray();
            var tasks = new List<Task>();
            tasks.AddRange(candidates.Select(session =>
                Task.Run(() => server.JoinField(session, field, false))));
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++) server.ListJoinableFields(0, 10);
            }));
            tasks.Add(Task.Run(() => server.TryCloseField(master, out _)));

            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(server.GetField(field.Id));
            Assert.Equal(-1, master.FieldId);
            Assert.All(candidates, session => Assert.Equal(-1, session.FieldId));
            Assert.Empty(field.Players);
        }

        [Fact]
        public void QuickJoin_SkipsStagePasswordAndLevelIneligibleRooms()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            server.CreateField(RoomOptions("stage", 0), NewSession(1, server));
            server.CreateField(RoomOptions("password", 1) with { Password = "pw" }, NewSession(2, server));
            server.CreateField(RoomOptions("level", 1) with { MinLevel = 20 }, NewSession(3, server));
            Field eligible = server.CreateField(RoomOptions("eligible", 1), NewSession(4, server));
            var viewer = NewSession(5, server);
            viewer.CharLevel = 10;

            Assert.True(server.TryQuickJoinField(viewer, out Field? joined));
            Assert.Same(eligible, joined);
        }

        [Theory]
        [InlineData(1, 2, MatchPhase.RoundEnd, true)]
        [InlineData(0, 2, MatchPhase.RoundEnd, false)]
        [InlineData(1, 1, MatchPhase.RoundEnd, false)]
        [InlineData(1, 2, MatchPhase.Playing, false)]
        public void GamePointGateMatchesOriginalFieldState(
            byte mode, byte state, MatchPhase phase, bool expected)
        {
            var field = new Field(3) { Mode = mode, State = state, Phase = phase };

            Assert.Equal(expected, field.CanAcceptGamePoint());
        }

        [Theory]
        [InlineData(3, 0, 0, 1, 2)]
        [InlineData(3, 0, 10, 1, 1)]
        [InlineData(4, 1, 0, 1, 1)]
        [InlineData(1, 1, 10, 1, 2)]
        [InlineData(2, 0, 0, 1, 3)]
        [InlineData(3, 0, 0, 0, 3)]
        public void GamePointOutcomeMatchesOriginalSeatAndLosingSideRule(
            byte mode, byte losingSide, byte seat, ushort marker, byte expected)
        {
            var field = new Field(3) { Mode = mode, LosingSideWire = losingSide };

            Assert.Equal(expected, field.GamePointOutcome(seat, marker));
        }

        private static (Field Field, ClientSession Master, ClientSession Member) CreateRoom()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var master = NewSession(1, server);
            var member = NewSession(2, server);
            var field = new Field(3) { State = 1, MaxPlayers = 8 };
            field.Add(master);
            field.AssignSeat(master);
            field.Master = master;
            field.MasterSlot = 0;
            field.Add(member);
            field.AssignSeat(member);
            return (field, master, member);
        }

        private static (WorldServer Server, Field Field, ClientSession Master, ClientSession Member)
            CreateServerRoom()
        {
            var config = new WorldConfig();
            var server = new WorldServer(config, new WorldDatabase(config.Db));
            var master = NewSession(1, server);
            var member = NewSession(2, server);
            Field field = server.CreateField(RoomOptions("test", (byte)GameMode.TeamDeath), master);
            Assert.True(server.JoinField(member, field, false));
            field.State = 2;
            field.Phase = MatchPhase.Playing;
            field.Slots[0].State = 4;
            field.Slots[1].State = 4;
            master.Status = UserStatus.InField;
            member.Status = UserStatus.InField;
            return (server, field, master, member);
        }

        private static ClientSession NewSession(ushort slot, WorldServer server) =>
            new(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp),
                slot, server);

        private static void SetPlaying(Field field, int seat, ClientSession session)
        {
            field.Slots[seat].Session = session;
            field.Slots[seat].State = 4;
        }

        private static RoomCreationOptions RoomOptions(string name, byte mode) => new()
        {
            Name = name,
            MapId = 1,
            Mode = mode,
            MinLevel = 1,
            MaxLevel = 99
        };
    }
}
