using System;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Grupo "sala-B" dos handlers reconstruidos do worldserv.exe v258. Reconstrucao
    /// FIEL dos FUN_xxxx do dispatch FUN_0042ab40 (handlers.out.txt / gameplay.out.txt):
    ///
    ///   0x4f FUN_00424a20  Die/GameDiePlayer   — o player reporta a propria morte (FUN_004087d0)
    ///   0x50 FUN_00424b60  GameResult/scoring  — credita exp/gold do fim de partida
    ///
    /// Observacao: as rotas de sala/lista usam os handlers canônicos de ClientSession.Rooms;
    /// 0x47 permanece em WorldHandlers.Field.cs.
    ///
    /// Convencao da fundacao: Op_0xNN_Recon(HandlerContext ctx). Helpers compartilhados
    /// (ReconField/ReconRec/SendField/SendLobbyMsg + Field.BroadcastField/BroadcastFieldPlaying/
    /// BroadcastLobby/ApplyReportedDeath) em WorldHandlers.Recon.cs / Domain.Field.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>
        /// FUN_00424a20 (opcode 0x4f) — Die/GameDiePlayer: o player reporta a PROPRIA morte.
        /// Gate: user+0x1460!=0 && user+0x14a4!=0 (DISC 0x8f); user+0x1440=='\x03' (DISC 0x90).
        /// FUN_0040b7d0 -> (fieldSlot, seat=local_4[0]). parse: cause=param_3[0] (<=8, senao
        /// DISC 0x91); b=param_3[1] (<=0x13, senao DISC 0x92). Se field+0x119 (modo) em {2,3}
        /// zera os contadores de tiro do user (user+0x2395/+0x2399). FUN_004087d0(field, seat,
        /// cause, b) = GameDiePlayer: marca dead, credita o killer/score, broadcast 0x4f (7B)
        /// a todos os players state==4, e pode encerrar o round (frag-limit -> 0x4a).
        /// </summary>
        internal static void Op_0x4F_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // gate (DISC 0x8f) — in-field && field-secondary
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x8f); return; }
            // status InField (DISC 0x90)
            if (u.Status != UserStatus.InField) { u.Disconnect(0x90); return; }

            byte cause = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // param_3[0]
            if (cause > 8) { u.Disconnect(0x91); return; }
            byte killer = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // param_3[1]
            if (killer > 0x13) { u.Disconnect(0x92); return; }

            var rec = ReconRec(ctx, out var field);
            if (field == null || rec == null)
            {
                Log.Warn("field", "[{0}] 0x4f Die sem field/seat", u.Slot);
                return;
            }
            int seat = rec.Slot;

            lock (field.SyncRoot)
            {

                // modo 2/3: zera contadores de tiro do user (user+0x2395/+0x2399) — anti-flood de disparo.
                if (field.Mode == 2 || field.Mode == 3)
                {
                    u.ActionCounterA = 0; u.ActionCounterB = 0;
                }

                // FUN_004087d0(field, victim=seat, cause, killer) — GameDiePlayer.
                // Pre-condicoes do original: field+8==2 (em jogo) && field+0x2b4==1 (playing) &&
                // playerRec.state==4 && !dead. So processa se valido.
                if (field.State != 2 || field.Phase != MatchPhase.Playing || rec.State != 4 || rec.Dead)
                {
                    Log.Debug("field", "[{0}] 0x4f Die ignorado (estado field={1} fase={2} rec={3} dead={4})",
                        u.Slot, field.State, field.Phase, rec.State, rec.Dead);
                    return;
                }

                DeathReportResult result = field.ApplyReportedDeath(seat, killer, cause);
                if (!result.Processed) return;

                // broadcast 0x4f (7B): [4f 00][victimSeat][cause][killerSeat][score0][score1]
                // a todos os players state==4 (FUN_0041b8a0 loop, len 7).
                byte[] body = FieldLifecycleFrames.Death(
                    (byte)seat, cause, killer, result.ScoreA, result.ScoreB);
                field.BroadcastFieldPlaying(0x4f, body);

                Log.Ok("field", "[{0}] 0x4f Die — seat {1} cause {2} killer {3} (scoreA={4} scoreB={5})",
                    u.Slot, seat, cause, killer, result.ScoreA, result.ScoreB);

                // frag-limit atingido -> ApplyReportedDeath ja iniciou RoundEnd; emite 0x4a.
                // golden source / layout validado em Field.Build0x4a() (antes este sitio invertia a ordem).
                if (field.Phase == MatchPhase.RoundEnd)
                    field.BroadcastFieldPlaying(0x4a, field.Build0x4a());
            }
        }

        /// <summary>
        /// FUN_00424b60 (opcode 0x50) — GameResult/scoring (exp/gold de fim de partida).
        /// Gate: user+0x1460!=0 && user+0x14a4!=0 (DISC 0x93); user+0x1440=='\x03' (DISC 0x94).
        /// Requer field+0x119!=0 && field+8==2 && field+0x2b4==2 (fim-round; senao DISC 0x95).
        /// parse: exp=param_3[0] (u32), gold=param_3[1] (u32), b=param_3[2], pos floats @9.., short @0x15.
        /// Se user+0x236c (ExpBonus) ativo: exp = exp*3/2. FUN_0041cf80 = anti-cheat
        /// ("Wrong Game Point! Exp/Gold" -> DISC 0x97); senao credita (FUN_004061c0 score,
        /// FUN_0040d300 level-up), LOBBY 0x51 quando sobe nível e FIELD subtype 0x0A com
        /// corpo de 60 bytes (o argumento 0x40 de FUN_0041B940 é comprimento, não subtype).
        /// </summary>
        internal static void Op_0x50_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // gate (DISC 0x93)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x93); return; }
            // status (DISC 0x94)
            if (u.Status != UserStatus.InField) { u.Disconnect(0x94); return; }

            var rec = ReconRec(ctx, out var field);
            if (field == null || rec == null)
            {
                Log.Warn("field", "[{0}] 0x50 GameResult sem field/seat", u.Slot);
                return;
            }
            int seat = rec.Slot;

            // FUN_00424B60 exige modo competitivo, field+8=2 e field+0x2B4=2 (RoundEnd).
            // Reporte tardio ou durante Playing é violação de estado, não crédito tolerado.
            if (!field.CanAcceptGamePoint())
            {
                Log.Warn("field", "[{0}] 0x50 fora de estado (mode={1} state={2} fase={3}) -> DISC 0x95",
                    u.Slot, field.Mode, field.State, field.Phase);
                u.Disconnect(0x95);
                return;
            }

            if (!ctx.P.CanRead(23))
            {
                Log.Warn("field", "[{0}] 0x50 truncado ({1} bytes) -> DISC 0x97",
                    u.Slot, ctx.Raw.Length);
                u.Disconnect(0x97);
                return;
            }

            uint rawExp = ctx.P.UInt32();   // param_3[0]
            uint gold = ctx.P.UInt32();     // param_3[1]
            byte flag = ctx.P.Byte();       // *(param_3+2)
            uint metricA = ctx.P.UInt32();  // payload @9
            uint metricB = ctx.P.UInt32();  // payload @13
            uint metricC = ctx.P.UInt32();  // payload @17
            ushort tail = ctx.P.UInt16();   // payload @21

            // O original aplica o bônus de EXP antes de FUN_0041CF80; gold permanece bruto.
            uint exp = u.BonusExp(rawExp);
            if (!GamePointRules.IsValid(field.Mode, field.Round, field.MaxRounds, exp, gold))
            {
                Log.Warn("field", "[{0}] Wrong Game Point! Exp:{1} Gold:{2} -> DISC 0x97", u.Slot, exp, gold);
                u.Disconnect(0x97);
                return;
            }

            var identity = new GamePointIdentity(
                field.MatchId, field.Round, u.ActiveCharId, u.GameInfoId);
            var target = new GamePointTarget(field, rec, (byte)seat);
            var gamePoint = new CompetitiveGamePoint(
                identity, target, new GamePointAward(exp, gold), [metricA, metricB, metricC]);
            var aux = new GamePointAux(flag, metricA, metricB, metricC, tail);
            _ = CompleteGamePointAsync(ctx, gamePoint, aux);
        }

        private static async Task CompleteGamePointAsync(
            HandlerContext ctx, CompetitiveGamePoint gamePoint, GamePointAux aux)
        {
            GamePointSettlementResult result =
                await ctx.World.ApplyCompetitiveGamePointAsync(ctx.User, gamePoint);
            if (result.Status == GamePointSettlementStatus.Failed || !ctx.User.Connected) return;

            if (result.LevelUps > 0)
            {
                CharacterProgressionState progression = result.Progression;
                SendLobbyMsg(ctx, 0x51, ProgressionResponseBodies.LevelUp(
                    progression.Level, progression.LevelPoint));
            }

            LifetimeMatchRecord record = gamePoint.Field.ProjectGamePointRecord(
                gamePoint.Seat, aux.Tail,
                new LifetimeMatchRecord(
                    ctx.User.CharWin, ctx.User.CharLose, ctx.User.CharDraw));
            var snapshot = new GamePointResultSnapshot(
                ctx.User.GameInfoId, gamePoint.Gold, ctx.User.ActiveCharId,
                result.Progression, record.Win, record.Lose, record.Draw,
                ctx.User.GetGamePointCellStates());
            SendField(ctx, GamePointResultFrames.MessageType, GamePointResultFrames.Build(snapshot));

            Log.Ok("field", "[{0}] 0x50 GameResult {1} — exp={2} gold={3} " +
                "aux={4:X2}/{5:X8}/{6:X8}/{7:X8}/{8:X4} (seat {9} field {10})",
                ctx.User.Slot, result.Status, gamePoint.Exp, gamePoint.Gold,
                aux.Flag, aux.MetricA, aux.MetricB, aux.MetricC, aux.Tail,
                gamePoint.Seat, gamePoint.Field.Id);
        }

        private readonly record struct GamePointAux(
            byte Flag, uint MetricA, uint MetricB, uint MetricC, ushort Tail);

    }
}
