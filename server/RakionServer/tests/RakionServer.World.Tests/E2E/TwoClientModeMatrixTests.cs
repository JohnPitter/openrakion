using System;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Matriz dos modos PvP com dois clientes headless: cada modo (Golem/TeamDeath/
    /// Deathmatch/Boss) é criado, o segundo jogador entra, fica ready e o master inicia —
    /// tudo no fio, provando que os quatro modos armam a partida. Um caso negativo confirma
    /// que fragLimit fora da faixa do Deathmatch é rejeitado.
    /// </summary>
    [Collection("E2E")]
    public sealed class TwoClientModeMatrixTests
    {
        [Theory]
        [InlineData((byte)GameMode.Golem)]
        [InlineData((byte)GameMode.TeamDeath)]
        [InlineData((byte)GameMode.Deathmatch)]
        [InlineData((byte)GameMode.Boss)]
        public async Task EachMode_CreatesJoinsAndArms(byte mode)
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return; // skip suave
            var server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "master");
            await using var joiner = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "joiner");

            HeadlessWorldClient.RoomSpec spec = ((GameMode)mode) switch
            {
                GameMode.Golem => HeadlessWorldClient.RoomSpec.Golem("m-golem"),
                GameMode.TeamDeath => HeadlessWorldClient.RoomSpec.TeamDeath("m-team"),
                GameMode.Deathmatch => HeadlessWorldClient.RoomSpec.Deathmatch("m-dm"),
                GameMode.Boss => HeadlessWorldClient.RoomSpec.Boss("m-boss"),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };

            var (_, js, field) = JourneyHelper.DriveToArmedMatch(server, master, joiner, spec);

            Assert.Equal(mode, field.Mode);
            Assert.NotEqual(Guid.Empty, field.MatchId);
            Assert.Equal(MatchPhase.Pre, field.Phase);
            Assert.Equal(field.Id, js.FieldId);
        }

        [Fact]
        public async Task Deathmatch_WithFragLimitOutOfRange_IsRejected()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            var server = fixture.Server!;

            await using var master = await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, "master");

            master.Login("test", "test");
            master.WaitForFirstByte(0x0C, JourneyHelper.Timeout);
            master.SelectCharacter(1);
            ClientSession ms = JourneyHelper.WaitForSession(server, "test",
                s => s.ActiveCharId > 0 && s.Status == UserStatus.FieldLobby);

            // Deathmatch (mode 2) exige fragLimit em 0x0d..0x1e (13..30); 5 está fora → disconnect 0xCC.
            master.CreateRoom(new HeadlessWorldClient.RoomSpec("m-bad", 0, 2, 1, 432, 5, 1, 99));

            // A sessão não deve ganhar sala; o servidor derruba (0xCC).
            await Task.Delay(1000);
            Assert.True(ms.FieldId < 0, "sala inválida não deveria ter sido criada");
        }
    }
}
