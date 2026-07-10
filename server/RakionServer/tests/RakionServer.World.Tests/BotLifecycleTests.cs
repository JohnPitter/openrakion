using System;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Ciclo de vida do BOT no domínio (sem rede): alocação por time, contagem (bot ≠ humano),
    /// fallback de time cheio, reset de HP por round e remoção/limpeza. Garante as invariantes que o
    /// serviço (WorldServer.AddBotToField/DiscardBots) e o motor de IA dependem — tudo de raiz,
    /// sem ClientSession nem wire.
    /// </summary>
    public class BotLifecycleTests
    {
        private static Field NewField() => new Field(1) { Mode = 1, MaxRounds = 1, MinLevel = 1, MaxLevel = 10 };

        [Fact]
        public void AddBot_PlacesInRequestedTeamBlock()
        {
            var f = NewField();
            var t0 = f.AddBot("Rok", 5, 1, team: 0);
            var t1 = f.AddBot("Ares", 5, 1, team: 1);

            Assert.NotNull(t0);
            Assert.NotNull(t1);
            Assert.InRange(t0!.Value.Seat, 0, 9);     // time 0 = slots 0..9
            Assert.InRange(t1!.Value.Seat, 10, 0x13); // time 1 = slots 10..19
            Assert.Equal(0, t0.Value.Bot.Team);
            Assert.Equal(1, t1.Value.Bot.Team);
        }

        [Fact]
        public void Bot_IsOccupantButNotHuman()
        {
            var f = NewField();
            var added = f.AddBot("Karion", 5, 1, team: 1);

            var rec = f.RecAt(added!.Value.Seat)!;
            Assert.True(rec.IsBot);
            Assert.True(rec.Occupied);
            Assert.Equal(3, rec.State);          // ready (mesmo estado de um humano recém-alocado)
            Assert.Equal(1, f.BotCount);
            Assert.Equal(0, f.HumanCount);       // bots não entram em Players
            Assert.False(f.HasHuman);            // só bots -> nenhuma sala viva por bot
        }

        [Fact]
        public void AssignBotSeat_FallsBackToOtherTeam_WhenRequestedFull()
        {
            var f = NewField();
            for (int i = 0; i < 10; i++) Assert.NotNull(f.AddBot($"b{i}", 5, 1, team: 0)); // lota o time 0

            var overflow = f.AddBot("extra", 5, 1, team: 0);   // time 0 cheio -> cai no time 1
            Assert.NotNull(overflow);
            Assert.InRange(overflow!.Value.Seat, 10, 0x13);
            Assert.Equal(11, f.BotCount);
        }

        [Fact]
        public void StartRound_RestoresBotHp_AndPromotesToPlaying()
        {
            var f = NewField();
            var added = f.AddBot("Vyl", 5, 1, team: 1);
            var bot = added!.Value.Bot;
            bot.Hp = 1; bot.Dead = true; bot.SpawnedThisRound = true;

            f.StartRound();

            Assert.Equal(bot.MaxHp, bot.Hp);
            Assert.False(bot.Dead);
            Assert.False(bot.SpawnedThisRound);          // re-anuncia o spawn no novo round
            Assert.Equal(4, f.RecAt(added.Value.Seat)!.State); // ready -> playing
        }

        [Fact]
        public void RemoveAllBots_ClearsSeats_AndReturnsCount()
        {
            var f = NewField();
            f.AddBot("a", 5, 1, team: 0);
            f.AddBot("b", 5, 1, team: 1);

            int removed = f.RemoveAllBots();

            Assert.Equal(2, removed);
            Assert.Equal(0, f.BotCount);
            foreach (var r in f.Slots) Assert.False(r.IsBot);
        }

        [Fact]
        public void ClearBotSeat_EmptiesOnlyThatSeat()
        {
            var f = NewField();
            int a = f.AddBot("a", 5, 1, team: 0)!.Value.Seat;
            int b = f.AddBot("b", 5, 1, team: 1)!.Value.Seat;

            f.ClearBotSeat(a);

            Assert.False(f.RecAt(a)!.IsBot);
            Assert.True(f.RecAt(b)!.IsBot);
            Assert.Equal(1, f.BotCount);
        }

        [Fact]
        public void EphemeralBotId_IsUniquePerField()
        {
            var f = NewField();
            var a = f.AddBot("a", 5, 1, team: 0)!.Value.Bot;
            var b = f.AddBot("b", 5, 1, team: 1)!.Value.Bot;
            Assert.NotEqual(a.Id, b.Id);
        }

        [Fact]
        public void PatrolStep_MovesZ_AndInvertsAtBounds()
        {
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 1);   // time 1: faixa [-24..-2], começa em -24 indo +Z
            bot.InitStagePosition();
            Assert.Equal(-24f, bot.Z);

            float z0 = bot.Z;
            bot.PatrolStep();
            Assert.True(bot.Z > z0);                            // anda em direção ao centro (+Z)

            // anda até bater no limite superior: clampa em -2 (sem ultrapassar) e inverte a direção.
            float prev = bot.Z;
            while (bot.Z < -2f) { prev = bot.Z; bot.PatrolStep(); }
            Assert.Equal(-2f, bot.Z);                           // clampou exatamente no limite
            Assert.True(prev < -2f);                            // veio de baixo (estava avançando)

            bot.PatrolStep();
            Assert.True(bot.Z < -2f);                           // direção invertida no limite: agora volta (-Z)

            for (int i = 0; i < 200; i++)                       // vai-e-volta longo: nunca escapa da faixa do time
            {
                bot.PatrolStep();
                Assert.InRange(bot.Z, -24f, -2f);
            }
        }

        [Fact]
        public void MoveToward_MovesCloserToTarget()
        {
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 1);
            bot.InitStagePosition();   // Z=-24, X=3.75
            float z0 = bot.Z;

            bot.MoveToward(3.75f, 0f);   // alvo no centro

            Assert.True(bot.Z > z0);   // moveu em direção ao centro (+Z)
            Assert.Equal(3.75f, bot.X); // X não muda (alvo no mesmo X)
        }

        [Fact]
        public void MoveToward_DoesNotOvershootCloseTarget()
        {
            var bot = new BotPlayer(2, "Ares", 5, 1, team: 1) { X = 0, Z = 0 };

            bot.MoveToward(0.5f, 0.5f);   // alvo a ~0.7 coord (< 1.5 = range de "perto")

            Assert.Equal(0f, bot.X);   // não se moveu: já perto o suficiente
            Assert.Equal(0f, bot.Z);
        }

        [Fact]
        public void MoveToward_UpdatesYawToFaceTarget()
        {
            var bot = new BotPlayer(3, "Vyl", 5, 1, team: 1) { X = 0, Z = 0, Yaw = 0 };

            bot.MoveToward(10f, 0f);   // alvo em +X

            Assert.True(bot.Yaw > 80f && bot.Yaw < 100f);   // ~90 graus (atan2(10,0))
        }

        [Fact]
        public void SetAimToward_PointsAtTarget()
        {
            var bot = new BotPlayer(4, "Drak", 5, 1, team: 1) { X = 0, Y = 0, Z = 0 };

            bot.SetAimToward(6f, 0f, 0f);   // alvo em +X, distância 6

            Assert.True(bot.AimX > 5.5f);   // apontando em +X (~6.0 normalizado)
            Assert.InRange(bot.AimY, -0.1f, 0.1f);
            Assert.InRange(bot.AimZ, -0.1f, 0.1f);
        }

        [Fact]
        public void SetAimToward_NormalizesDirection()
        {
            var bot = new BotPlayer(5, "Nyx", 5, 1, team: 1) { X = 0, Y = 0, Z = 0 };

            bot.SetAimToward(3f, 0f, 4f);   // alvo em (3,0,4), distância 5

            float len = System.MathF.Sqrt(bot.AimX * bot.AimX + bot.AimY * bot.AimY + bot.AimZ * bot.AimZ);
            Assert.InRange(len, 5.5f, 6.5f);   // magnitude ~6.0 (aimScale)
        }

        [Fact]
        public void Stagger_StunsForDuration_AndInterruptsCombo()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { ComboStep = 2 };
            bot.Stagger(now: 1000, durationMs: 360);
            Assert.True(bot.IsStunned(1000));    // atordoado no início
            Assert.True(bot.IsStunned(1359));    // dentro da janela
            Assert.False(bot.IsStunned(1360));   // expira no fim
            Assert.Equal(0, bot.ComboStep);      // combo interrompido pelo golpe
        }

        [Fact]
        public void ApplyKnockback_PushesAwayFromAttacker()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { X = 5f, Z = 0f };
            bot.ApplyKnockback(fromX: 0f, fromZ: 0f, dist: 2f);   // atacante na origem, bot em +X
            Assert.Equal(7f, bot.X, 3);    // empurrado p/ +X (5 + 2, longe do atacante)
            Assert.Equal(0f, bot.Z, 3);    // mesmo eixo
        }

        [Fact]
        public void ApplyKnockback_DegenerateWhenColocated()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { X = 3f, Z = 3f };
            bot.ApplyKnockback(fromX: 3f, fromZ: 3f, dist: 2f);   // atacante NA MESMA posição
            Assert.Equal(3f, bot.X, 3);    // sem direção em X
            Assert.Equal(1f, bot.Z, 3);    // empurra p/ -Z por convenção (3 + (-1)*2 = 1)
        }

        // ---- combate humano->bot (arbitragem server-side; o bot não reporta a própria morte) ----

        [Fact]
        public void TakeDamage_ReturnsTrueOnlyOnLethalHit()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { MaxHp = 100, Hp = 100 };
            Assert.False(bot.TakeDamage(40)); Assert.Equal(60, bot.Hp); Assert.False(bot.Dead);
            Assert.False(bot.TakeDamage(40)); Assert.Equal(20, bot.Hp);
            Assert.True(bot.TakeDamage(40));  Assert.Equal(0, bot.Hp); Assert.True(bot.Dead);   // golpe letal
            Assert.False(bot.TakeDamage(40)); // já morto: idempotente, sem "ressuscitar morte"
        }

        /// <summary>Monta um field PvP em jogo com um humano (seat 0) posicionado e um bot inimigo (time 1).</summary>
        private static (Field f, int botSeat, BotPlayer bot, PlayerRec human) PvpFieldWithBot()
        {
            var f = new Field(1) { Mode = 1, MaxRounds = 1, MinLevel = 1, MaxLevel = 10 };
            var added = f.AddBot("Vyl", 5, 1, team: 1)!.Value;   // time 1 -> slot 10..19
            f.StartRound();                                      // bot -> Playing(4), Hp=MaxHp
            f.State = 2; f.Phase = MatchPhase.Playing;            // PvP em jogo (gate do hit)
            var human = f.RecAt(0)!;                              // seat 0 = time 0 (inimigo do bot)
            human.State = 4; human.LastX = 0; human.LastZ = 0; human.LastPositionMs = 1;
            human.LastAttackMs = Environment.TickCount64;
            human.LastHeading = 180;                             // olha p/ +Z (onde os bots são posicionados) — cone de mira
            return (f, added.Seat, added.Bot, human);
        }

        [Fact]
        public void ResolvePendingBotHitByHuman_KillsBotOnVectorIntersection()
        {
            var (f, botSeat, bot, _) = PvpFieldWithBot();
            bot.X = 0; bot.Z = 2;      // dentro do alcance (HumanMeleeRange 6.5)
            bot.Hp = 40;               // um golpe (dano 40) é letal

            int? dead = f.ResolvePendingBotHitByHuman(0, aimX: 0, aimZ: 4, hitBotSlot: out int hitSlot);

            Assert.Equal(botSeat, dead);
            Assert.Equal(botSeat, hitSlot);   // o out reporta qual bot levou o acerto
            Assert.True(bot.Dead);
            Assert.True(f.RecAt(botSeat)!.Dead);   // OnPlayerDeath marcou o rec
        }

        [Fact]
        public void ResolvePendingBotHitByHuman_MissesOutOfRange()
        {
            var (f, _, bot, _) = PvpFieldWithBot();
            bot.X = 0; bot.Z = 100;    // fora do alcance

            Assert.Null(f.ResolvePendingBotHitByHuman(0, aimX: 0, aimZ: 4, hitBotSlot: out int hitSlot));
            Assert.Equal(-1, hitSlot);   // errou -> nenhum bot sinalizado
            Assert.False(bot.Dead);
            Assert.Equal(bot.MaxHp, bot.Hp);   // sem dano
        }

        [Fact]
        public void ResolvePendingBotHitByHuman_ThrottlesRepeatedHits()
        {
            var (f, _, bot, human) = PvpFieldWithBot();
            bot.X = 0; bot.Z = 2;
            bot.Hp = 100;   // valor determinístico (independe do MaxHp de balanceamento)

            Assert.Null(f.ResolvePendingBotHitByHuman(0, 0, 4, out int firstHit));
            ushort afterFirst = bot.Hp;
            Assert.Equal(60, afterFirst);
            Assert.True(firstHit >= 0);                  // acerto não-fatal TAMBÉM é sinalizado (incrementa o HIT×N)
            human.LastAttackMs = Environment.TickCount64;
            Assert.Null(f.ResolvePendingBotHitByHuman(0, 0, 4, out int secondHit));
            Assert.Equal(afterFirst, bot.Hp);            // sem dano adicional
            Assert.Equal(-1, secondHit);                 // throttle -> nenhum sinal (não conta combo no cooldown)
        }

        [Fact]
        public void ResolvePendingBotHitByHuman_IgnoredOutsidePvpPlay()
        {
            var (f, _, bot, _) = PvpFieldWithBot();
            bot.X = 0; bot.Z = 2;
            f.Phase = MatchPhase.Pre;   // ainda em countdown -> não arbitra hit

            Assert.Null(f.ResolvePendingBotHitByHuman(0, 0, 4, out _));
            Assert.Equal(bot.MaxHp, bot.Hp);
        }

        [Fact]
        public void ResolvePendingBotHitByHuman_RequiresRecentAttack()
        {
            var (f, _, bot, human) = PvpFieldWithBot();
            bot.X = 0; bot.Z = 2;
            human.LastAttackMs = 0;

            Assert.Null(f.ResolvePendingBotHitByHuman(0, 0, 4, out int hitSlot));
            Assert.Equal(-1, hitSlot);
            Assert.Equal(bot.MaxHp, bot.Hp);
        }

        [Fact]
        public void ResolvePendingBotHitByHuman_DoesNotStealImpactFromCloserHuman()
        {
            var (f, _, bot, _) = PvpFieldWithBot();
            bot.X = 0; bot.Z = 3;
            var humanTarget = f.RecAt(11)!;
            humanTarget.State = 4;
            humanTarget.LastPositionMs = 1;
            humanTarget.LastX = 0;
            humanTarget.LastZ = 2;

            Assert.Null(f.ResolvePendingBotHitByHuman(0, 0, 4, out int hitSlot));
            Assert.Equal(-1, hitSlot);
            Assert.Equal(bot.MaxHp, bot.Hp);
        }

        [Fact]
        public void ResolvePendingBotHitByHuman_ProximityWithoutVectorIntersectionMisses()
        {
            var (f, _, bot, _) = PvpFieldWithBot();
            bot.X = 0; bot.Z = 2;

            Assert.Null(f.ResolvePendingBotHitByHuman(0, aimX: 4, aimZ: 0, out int hitSlot));
            Assert.Equal(-1, hitSlot);
            Assert.Equal(bot.MaxHp, bot.Hp);
        }

        [Fact]
        public void BotHitOnHuman_DoesNotSynthesizeDeath()
        {
            var (f, botSeat, bot, human) = PvpFieldWithBot();
            bot.TargetSeat = human.Slot;
            bot.X = 0; bot.Z = 2;
            human.HitCombo = 4;

            bool announced = f.TryAnnounceBotHitOnHuman(f.RecAt(botSeat)!);

            Assert.True(announced);
            Assert.True(human.Playing);
            Assert.False(human.Dead);
            Assert.Equal(0u, f.RecAt(botSeat)!.Score);
            Assert.Equal(0, human.HitCombo);
        }

        [Fact]
        public void HumanMovement_ReconcilesClientAuthoritativeRespawn()
        {
            var (f, _, _, human) = PvpFieldWithBot();
            human.State = 1;
            human.Dead = true;

            bool reconciled = f.ReconcileHumanMovement(human);

            Assert.True(reconciled);
            Assert.True(human.Playing);
            Assert.False(human.Dead);
        }

        [Fact]
        public void HumanMovement_DoesNotReviveOutsidePlayingPhase()
        {
            var (f, _, _, human) = PvpFieldWithBot();
            human.State = 1;
            human.Dead = true;
            f.Phase = MatchPhase.RoundEnd;

            Assert.False(f.ReconcileHumanMovement(human));
            Assert.Equal(1, human.State);
            Assert.True(human.Dead);
        }

        // ---- respawn do bot no round (o bot não tem cliente p/ pedir o próprio respawn) ----

        [Fact]
        public void DueForRespawn_SchedulesOnDeath_ThenFiresAfterDelay()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { Dead = true };
            Assert.False(bot.DueForRespawn(1000, 5000));   // 1a vez: AGENDA (RespawnAtMs=6000), ainda não
            Assert.Equal(6000, bot.RespawnAtMs);
            Assert.False(bot.DueForRespawn(5999, 5000));   // antes do deadline
            Assert.True(bot.DueForRespawn(6000, 5000));    // no deadline -> renasce
        }

        [Fact]
        public void DueForRespawn_FalseWhileAlive()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { Dead = false };
            Assert.False(bot.DueForRespawn(99999, 5000));
            Assert.Equal(0, bot.RespawnAtMs);              // vivo: nem agenda
        }

        [Fact]
        public void Respawn_RestoresHp_AndRevivesInPlace()
        {
            var bot = new BotPlayer(1, "X", 5, 1, team: 1) { Hp = 0, Dead = true, RespawnAtMs = 6000, X = 30f, Z = -7f };
            bot.Respawn();
            Assert.Equal(bot.MaxHp, bot.Hp);
            Assert.False(bot.Dead);
            Assert.Equal(0, bot.RespawnAtMs);              // agendamento limpo
            Assert.Equal(30f, bot.X);                      // revive NO LUGAR (sem teleporte de spawn)
            Assert.Equal(-7f, bot.Z);
        }
    }
}
