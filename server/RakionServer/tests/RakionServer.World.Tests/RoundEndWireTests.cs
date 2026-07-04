using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Encoding de WIRE do fim de round (0x4a, corpo [causa/+0x2bd][winner/+0x2bf][wins0][wins1]),
    /// cravado do worldserv (0x405dc7/0x405e16, 0x40804e/0x4080a1, 0x405ade/0x405b25): **+0x2bf = 1
    /// quando o TIME 0 vence e 0 quando o TIME 1 vence** — invertido do índice do time. Regressão aqui
    /// = o VENCEDOR vê a tela de DERROTA (bug reportado in-game 2026-07-04, Golem War 1v1).
    /// </summary>
    public class RoundEndWireTests
    {
        private static Field NewMatch()
        {
            var f = new Field(1) { Mode = (byte)GameMode.Golem, State = 2, MaxPlayers = 12 };
            f.StartRound();
            return f;
        }

        [Fact]
        public void EndRound_Team0Wins_WireWinnerIs1()
        {
            var f = NewMatch();
            f.EndRound(winnerTeam: 0);
            byte[] b = f.Build0x4a();
            Assert.Equal(1, b[1]);          // +0x2bf: time0 venceu -> 1 (NÃO 0!)
            Assert.Equal(1, b[2]);          // wins0
            Assert.Equal(0, b[3]);          // wins1
        }

        [Fact]
        public void EndRound_Team1Wins_WireWinnerIs0()
        {
            var f = NewMatch();
            f.EndRound(winnerTeam: 1);
            byte[] b = f.Build0x4a();
            Assert.Equal(0, b[1]);          // +0x2bf: time1 venceu -> 0
            Assert.Equal(0, b[2]);
            Assert.Equal(1, b[3]);
        }

        [Fact]
        public void EndRound_Cause_GoesInByte0()
        {
            // +0x2bd = CAUSA (1=eliminação/objetivo, 2=placar/tempo), não o vencedor.
            var f = NewMatch();
            f.EndRound(winnerTeam: 0, cause: 2);
            Assert.Equal(2, f.Build0x4a()[0]);

            var g = NewMatch();
            g.EndRoundObjective(winnerTeam: 1);   // objetivo -> causa default 1
            Assert.Equal(1, g.Build0x4a()[0]);
            Assert.Equal(0, g.Build0x4a()[1]);    // time1 venceu -> wire 0
        }
    }
}
