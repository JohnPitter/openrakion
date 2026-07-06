using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Rota de OBJETIVO do bot no Golem War (domínio puro): o layout de waypoints por mapa/time e o dano
    /// server-authoritative aos golems — dourado neutro = GATE da rota (não encerra round); master INIMIGO
    /// = VITÓRIA do round via <see cref="Field.EndRoundObjective"/>. Trava a regra que a IA
    /// (<c>WorldServer.UpdateBotObjective</c>) consome.
    /// </summary>
    public class GolemObjectiveTests
    {
        [Fact]
        public void GravityLayout_RouteRunsCorridorToEnemyGolem_PerTeam()
        {
            var layout = GolemWarLayouts.For(mapId: 210);   // gravity
            var r0 = layout.RouteFor(0);
            var r1 = layout.RouteFor(1);

            // spawns nos extremos ±X do corredor
            Assert.Equal(50f, layout.SpawnFor(0).X);
            Assert.Equal(-50f, layout.SpawnFor(1).X);

            Assert.Equal(4, r0.Count);
            Assert.Equal(4, r1.Count);
            // corredor (Nav) até o centro
            Assert.Equal(WaypointKind.Nav, r0[0].Kind);
            Assert.Equal(WaypointKind.Nav, r0[1].Kind);
            // golem DOURADO neutro (Param 2) no centro — mesmo ponto p/ os dois times
            Assert.Equal(WaypointKind.Golem, r0[2].Kind); Assert.Equal(2, r0[2].Param);
            Assert.Equal(WaypointKind.Golem, r1[2].Kind); Assert.Equal(2, r1[2].Param);
            // último = golem INIMIGO (time 0 → master time 1; time 1 → master time 0), lados opostos
            Assert.Equal(WaypointKind.Golem, r0[3].Kind); Assert.Equal(1, r0[3].Param);
            Assert.True(r0[3].Pos.X < 0f);                                  // inimigo do RED no lado −X
            Assert.Equal(WaypointKind.Golem, r1[3].Kind); Assert.Equal(0, r1[3].Param);
            Assert.True(r1[3].Pos.X > 0f);                                  // inimigo do BLUE no lado +X
        }

        [Fact]
        public void DamageGoldGolem_ReturnsTrueOnlyWhenDepleted()
        {
            var f = new Field(1) { Mode = 1 };
            Assert.False(f.DamageGoldGolem(40)); Assert.Equal(60, f.GoldGolemHp);
            Assert.False(f.DamageGoldGolem(40)); Assert.Equal(20, f.GoldGolemHp);
            Assert.True(f.DamageGoldGolem(40));  Assert.Equal(0, f.GoldGolemHp);   // derrotado neste golpe
            Assert.False(f.DamageGoldGolem(40));                                   // idempotente após zerar
        }

        [Fact]
        public void DamageGolemTarget_GoldGate_DoesNotEndRound()
        {
            var f = new Field(1) { Mode = 1, MaxRounds = 3 };
            f.StartRound();

            Assert.True(f.DamageGolemTarget(2, 100));    // dourado derrotado (avança a rota)...
            Assert.Equal(MatchPhase.Playing, f.Phase);   // ...mas NÃO é win-condition: o round segue
        }

        [Fact]
        public void DamageGolemTarget_EnemyMaster_EndsRound_CreditingTeam()
        {
            var f = new Field(1) { Mode = 1, MaxRounds = 3 };
            f.StartRound();

            // bot do time 0 destrói o Master Golem do time 1 (target=1) -> time 0 vence o round
            Assert.True(f.DamageGolemTarget(1, 100));
            Assert.Equal(0, f.Golem1Hp);
            Assert.Equal(MatchPhase.RoundEnd, f.Phase);   // EndRoundObjective disparou
            Assert.Equal(1, f.Wins0);                     // crédito ao time 0
            Assert.Equal(0, f.Wins1);
        }

        [Fact]
        public void StartRound_ResetsGoldGolem()
        {
            var f = new Field(1) { Mode = 1, MaxRounds = 3 };
            f.DamageGoldGolem(100);
            Assert.Equal(0, f.GoldGolemHp);

            f.StartRound();
            Assert.Equal(100, f.GoldGolemHp);   // re-armado p/ o novo round
        }
    }
}
