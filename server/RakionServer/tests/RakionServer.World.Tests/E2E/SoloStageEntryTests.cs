using System;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Valida a ENTRADA em stage PvE solo pelo fio, com um cliente headless: login →
    /// char-select → criar sala solo (mode 0, stage 1) → start → spawn 0x4b, que dispara
    /// `BeginStageRun`. Prova que o servidor abre a execução de stage (identidade + stage
    /// ativo) para uma sessão de rede real. A liquidação 0x53 (com reward exato anti-cheat)
    /// é validada ponta a ponta por `SoloStageSettlementE2ETests`.
    /// </summary>
    [Collection("E2E")]
    public sealed class SoloStageEntryTests
    {
        [Fact]
        public async Task SoloStage_SpawnStartsStageRun()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave
            var server = fixture.Server!;

            await using var player = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "solo");

            // test2 → ProbeTwo (nível 10) cabe na faixa do stage 1 (min 1, max 10).
            player.Login("test2", "test2");
            player.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            player.SelectCharacter(9001);
            ClientSession s = JourneyHelper.WaitForSession(server, "test2",
                x => x.ActiveCharId > 0 && x.Status == UserStatus.FieldLobby);

            // Sala solo no stage 1 (mode 0 => stage; MapId = id do stage).
            player.CreateRoom(new HeadlessWorldClient.RoomSpec(
                "e2e-pve", Map: 1, Mode: 0, Rounds: 1, DurationSec: 432,
                FragLimit: 0, MinLevel: 1, MaxLevel: 99));
            JourneyHelper.WaitUntil(() => s.FieldId >= 0 && server.GetField(s.FieldId) != null, "sala solo não criada");
            Field field = server.GetField(s.FieldId)!;
            Assert.Equal(0, field.Mode);
            Assert.Equal(1, field.MapId);

            // Start promove a Status InField (necessário p/ o gate do 0x4b); spawn abre a run.
            player.StartMatch();
            JourneyHelper.WaitUntil(() => s.Status == UserStatus.InField, "não promoveu a InField");
            player.SpawnField();
            player.RoundStart();
            JourneyHelper.WaitUntil(() => s.StageRunId != Guid.Empty, "run de stage não iniciou");

            Assert.NotEqual(Guid.Empty, s.StageRunId);
            Assert.Equal(field.MatchId, s.StageRunId);
            Assert.Equal((byte)1, s.ActiveStageId);
        }
    }
}
