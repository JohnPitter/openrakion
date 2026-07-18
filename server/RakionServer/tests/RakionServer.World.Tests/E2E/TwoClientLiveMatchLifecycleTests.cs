using System;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Exercita o motor global real, sem chamar ApplyReportedDeath ou AdvanceLifecycle no teste:
    /// partida armada, engage por deadline, morte 0x4F no fio, fim de round e fim de match.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientLiveMatchLifecycleTests
    {
        [Fact]
        public async Task DeadlineAndDeathFrame_AdvanceRoundAndEndMatch()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            var server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "master");
            await using var joiner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "joiner");

            var (masterSession, joinerSession, field) =
                JourneyHelper.DriveToArmedOpposingTeamsMatch(
                    server, master, joiner, HeadlessWorldClient.RoomSpec.Golem("e2e-live-cycle"));

            master.SpawnField();
            JourneyHelper.WaitUntil(
                () => field.FindRec(masterSession)?.State == 4,
                "master não spawnou");
            Assert.Equal(MatchPhase.Pre, field.Phase);
            Assert.Equal((byte)3, field.FindRec(joinerSession)!.State);

            ExpireDeadline(field);

            JourneyHelper.WaitUntil(
                () => field.Phase == MatchPhase.Playing,
                "motor não iniciou o round pelo deadline");
            Assert.Equal((byte)4, field.FindRec(masterSession)!.State);
            Assert.Equal((byte)3, field.FindRec(joinerSession)!.State);

            joiner.SpawnField();
            JourneyHelper.WaitUntil(
                () => field.FindRec(joinerSession)?.State == 4,
                "joiner não entrou no round em andamento");

            byte masterSeat = (byte)masterSession.FieldSeat;
            byte joinerSeat = (byte)joinerSession.FieldSeat;
            joiner.ReportDeath(cause: 0, killerSeat: masterSeat);

            JourneyHelper.WaitUntil(
                () => field.Phase == MatchPhase.RoundEnd,
                "morte 0x4F não encerrou o round Golem");
            Assert.Equal((byte)1, field.Wins0);
            Assert.Equal((byte)0, field.Wins1);

            byte[] death = master.WaitFor(
                frame => IsFieldFrame(frame, 0x4f) && frame.Length >= 7 &&
                         frame[2] == joinerSeat && frame[4] == masterSeat,
                JourneyHelper.Timeout);
            Assert.Equal((byte)0, death[3]);
            master.WaitFor(frame => IsFieldFrame(frame, 0x4a), JourneyHelper.Timeout);

            ExpireDeadline(field);

            JourneyHelper.WaitUntil(() => field.State == 1, "motor não encerrou o match");
            master.WaitFor(frame => IsFieldFrame(frame, 0x44), JourneyHelper.Timeout);
            Assert.Equal((byte)2, field.Round);
        }

        private static bool IsFieldFrame(byte[] frame, ushort opcode) =>
            frame.Length >= 2 && frame[0] == (byte)opcode && frame[1] == (byte)(opcode >> 8);

        private static void ExpireDeadline(Field field)
        {
            lock (field.SyncRoot)
                field.DeadlineMs = Environment.TickCount64 - 1;
        }
    }
}
