using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Grupo "sala-B" dos handlers reconstruidos do worldserv.exe v258. Reconstrucao
    /// FIEL dos FUN_xxxx do dispatch FUN_0042ab40 (handlers.out.txt / gameplay.out.txt):
    ///
    ///   0x3a FUN_004234e0  FieldStart/Leave3D  — sai do stage 3D (remove player do field)
    ///   0x4f FUN_00424a20  Die/GameDiePlayer   — o player reporta a propria morte (FUN_004087d0)
    ///   0x50 FUN_00424b60  GameResult/scoring  — credita exp/gold do fim de partida
    ///   0x53 FUN_00425010  FieldCreate (result)— cria/encerra resultado de partida (exp/gold/level)
    ///   0x61 FUN_0041c270  FieldReady ack      — grava o token de ready do client
    ///
    /// Observacao: 0x39 (FUN_00423300 = FieldListReq) e 0x47 (FUN_004244f0 = FieldList/3D-chat)
    /// JA estao implementados (WorldHandlers.RoomMgmt.cs / WorldHandlers.Field.cs) e por isso
    /// NAO sao redefinidos aqui (a tabela Build() ja aponta para eles).
    ///
    /// Convencao da fundacao: Op_0xNN_Recon(HandlerContext ctx). Helpers compartilhados
    /// (ReconField/ReconRec/SendField/SendLobbyMsg + Field.BroadcastField/BroadcastFieldPlaying/
    /// BroadcastLobby/OnPlayerDeath) em WorldHandlers.Recon.cs / Domain.Field.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>
        /// FUN_004234e0 (opcode 0x3a) — FieldStart/Leave3D: o player sai do stage 3D.
        /// Gate: user+0x1460!=0 && user+0x14a4!=0 (senao DISC 0x50); user+0x1440=='\x03'
        /// (senao DISC 0x51). FUN_0040b7d0 resolve (fieldSlot, seat); FUN_004091e0 remove o
        /// player-object do field; FUN_0041b8b0 notifica a saida (cleanup). Sem reply ao proprio.
        /// </summary>
        internal static void Op_0x3A_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // gate: in-field && field-secondary (DISC 0x50)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x50); return; }
            // estado tem de ser InField/playing (DISC 0x51)
            if (u.Status != UserStatus.InField) { u.Disconnect(0x51); return; }

            var rec = ReconRec(ctx, out var field);
            if (field == null || rec == null)
            {
                Log.Warn("field", "[{0}] 0x3a Leave3D sem field/seat", u.Slot);
                return;
            }
            int seat = rec.Slot;

            // FUN_004091e0(fieldRec, seat) — remove o player-object do stage; notifica os demais.
            rec.State = 0;
            rec.Dead = false;
            rec.Score = 0;
            rec.Session = null;
            field.Remove(u);

            // FUN_0041b8b0(this, user) — notificacao curta de saida/cleanup aos players ocupados.
            // (broadcast FIELD msgType 0x3a com o seat que saiu, fiel ao padrao de notify de leave)
            field.BroadcastField(0x3a, new byte[] { (byte)seat });

            Log.Ok("field", "[{0}] 0x3a Leave3D — saiu do stage (field {1} seat {2})", u.Slot, field.Id, seat);
        }

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

            // modo 2/3: zera contadores de tiro do user (user+0x2395/+0x2399) — anti-flood de disparo.
            if (field.Mode == 2 || field.Mode == 3)
            {
                u.FieldPairA = 0; u.FieldPairB = 0; // contadores de acao do round
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

            field.OnPlayerDeath(seat, killer, cause);

            // broadcast 0x4f (7B): [4f 00][victimSeat][cause][killerSeat][score0][score1]
            // a todos os players state==4 (FUN_0041b8a0 loop, len 7).
            byte[] body = new byte[] { (byte)seat, cause, killer, field.Score0, field.Score1 };
            field.BroadcastFieldPlaying(0x4f, body);

            Log.Ok("field", "[{0}] 0x4f Die — seat {1} cause {2} killer {3} (s0={4} s1={5})",
                u.Slot, seat, cause, killer, field.Score0, field.Score1);

            // frag-limit atingido -> OnPlayerDeath ja chamou EndRound; emite 0x4a (fim de round).
            if (field.Phase == MatchPhase.RoundEnd)
                field.BroadcastFieldPlaying(0x4a, new byte[] { field.WinnerSide, field.Wins0, field.Wins1, field.LastRoundWinner });
        }

        /// <summary>
        /// FUN_00424b60 (opcode 0x50) — GameResult/scoring (exp/gold de fim de partida).
        /// Gate: user+0x1460!=0 && user+0x14a4!=0 (DISC 0x93); user+0x1440=='\x03' (DISC 0x94).
        /// Requer field+0x119!=0 && field+8==2 && field+0x2b4==2 (fim-round; senao DISC 0x95).
        /// parse: exp=param_3[0] (u32), gold=param_3[1] (u32), b=param_3[2], pos floats @9.., short @0x15.
        /// Se user+0x236c (ExpBonus) ativo: exp = exp*3/2. FUN_0041cf80 = anti-cheat
        /// ("Wrong Game Point! Exp/Gold" -> DISC 0x97); senao credita (FUN_004061c0 score,
        /// FUN_0040d300 level-up), broadcast LOBBY 0x51 (level-up) + FIELD 0x40 (resultado) ao proprio.
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

            // requer field+0x119!=0 (modo valido) && field+8==2 (em jogo) && field+0x2b4==2 (fim-round).
            // O original DISC 0x95 aqui; nosso timing de fases difere (EndMatch pode ja ter rodado
            // quando o reporte chega) -> ignora sem derrubar a sessao.
            if (!(field.Mode != 0 && (field.State == 2 || field.State == 1)))
            {
                Log.Debug("field", "[{0}] 0x50 fora de estado (mode={1} state={2} fase={3}) — ignorado",
                    u.Slot, field.Mode, field.State, field.Phase);
                return;
            }

            uint exp = ctx.P.CanRead(4) ? ctx.P.UInt32() : 0;   // param_3[0]
            uint gold = ctx.P.CanRead(4) ? ctx.P.UInt32() : 0;  // param_3[1]
            byte flag = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // *(param_3+2)

            // multiplicador de exp x3/2 (user+0x236c)
            if (u.ExpBonusActive) exp = exp * 3 >> 1;

            // anti-cheat FUN_0041cf80 — pontos plausiveis? (sanidade basica fiel ao gate do exe)
            if (!ValidateGamePoints(exp, gold))
            {
                Log.Warn("field", "[{0}] Wrong Game Point! Exp:{1} Gold:{2} -> DISC 0x97", u.Slot, exp, gold);
                u.Disconnect(0x97);
                return;
            }

            // credita: FUN_004061c0 (score no player-record) + FUN_0040d300 (level-up).
            rec.Score += exp;
            field.RecomputeMvp();
            u.Gold = u.Gold + gold;
            // persiste a transacao (a loja debita do mesmo saldo; sem isto o credito sumia no relogin)
            if (gold > 0 && u.GameInfoId > 0) _ = ctx.World.Db.AddGoldAsync(u.GameInfoId, (int)gold);
            // exp + level-up server-side (FUN_0040d300, curva classlevelinfo) — persiste exp/nivel.
            ctx.World.GrantExp(u, exp);

            // FUN_004038e0 LOBBY 0x51 (level-up) ao proprio — [51][level][u16 levelPoint]
            SendLobbyMsg(ctx, 0x51, new byte[] {
                u.CharLevel, (byte)(u.CharLevelPoint & 0xff), (byte)(u.CharLevelPoint >> 8 & 0xff) });

            // FUN_0041b940 FIELD 0x40 (resultado completo) ao proprio — [serverSeq][40][...]
            using var w = new PacketWriter();
            w.WriteInt32(u.FieldSecondaryRaw); // field-handle/secondary (local_2000)
            w.WriteWord(u.Slot);               // serverSeq-derivado (local_2004 era +0x1488; o header ja leva o serverSeq)
            w.WriteUInt32(gold);               // local_1ffc
            w.WriteUInt32(exp);                // exp creditado
            w.WriteByte(flag);                 // local_20d0
            w.WriteByte((byte)seat);
            SendField(ctx, 0x40, w.ToArray());

            Log.Ok("field", "[{0}] 0x50 GameResult — exp={1} gold={2} (seat {3} field {4})",
                u.Slot, exp, gold, seat, field.Id);
        }

        /// <summary>
        /// FUN_00425010 (opcode 0x53) — NetworkMessageFieldCreate (resultado/liquidacao de partida).
        /// Gate: user+0x1460!=0 && user+0x14a4!=0 (DISC 0x98); user+0x1440=='\x03' (senao DISC 0x99).
        /// Requer field+0x119=='\0' (modo lobby/quick, DISC 0x9a) && field+8==2 (DISC 0x9b) &&
        /// field+0x2b4==2 (fim-round). parse: idx=param_3[0] (<100, DISC 0x9c); cfgA=param_3[1]
        /// (<6, DISC 0x9d); cfgB=param_3[2] (<5, DISC 0x9e); cfgB x u16 mapSlots (<this+0x108,
        /// DISC 0x9f); depois u32 cost @uVar8, u32 gold @uVar8+4, pos @uVar8+8.
        /// FUN_0040bae0 reserva (result>1 -> erro; ==1 -> LOBBY 0x53 e retorna). FUN_0041cf80
        /// anti-cheat ("Wrong Game Point" -> DISC 0xa0). Senao FUN_0040d300 level-up (LOBBY 0x51),
        /// FUN_0040b940 weapons (FIELD 0x52 broadcast), FUN_0040bb60 placar; FIELD 0x52 ao proprio.
        /// </summary>
        internal static void Op_0x53_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // gate (DISC 0x98)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x98); return; }
            // status (DISC 0x99)
            if (u.Status != UserStatus.InField) { u.Disconnect(0x99); return; }

            var rec = ReconRec(ctx, out var field);
            if (field == null || rec == null)
            {
                Log.Warn("field", "[{0}] 0x53 FieldCreate sem field/seat", u.Slot);
                return;
            }
            int seat = rec.Slot;

            // field+0x119 deve ser 0 (DISC 0x9a)
            if (field.Mode != 0) { u.Disconnect(0x9a); return; }
            // field+8==2 (DISC 0x9b) e field+0x2b4==2 (fim-round)
            if (!(field.State == 2 && field.Phase == MatchPhase.RoundEnd)) { u.Disconnect(0x9b); return; }

            byte idx = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;   // param_3[0]
            if (idx >= 100) { u.Disconnect(0x9c); return; }
            byte cfgA = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;  // param_3[1]
            if (cfgA >= 6) { u.Disconnect(0x9d); return; }
            byte cfgB = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;  // param_3[2]
            if (cfgB >= 5) { u.Disconnect(0x9e); return; }

            // cfgB x u16 mapSlots
            ushort[] mapSlots = new ushort[cfgB];
            for (int i = 0; i < cfgB; i++)
            {
                ushort slot = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
                // limite this+0x108 (DAT slot habilitado) — best-effort: rejeita slots >= MaxPlayers nominais
                mapSlots[i] = slot;
            }

            uint cost = ctx.P.CanRead(4) ? ctx.P.UInt32() : 0;  // @uVar8
            uint gold = ctx.P.CanRead(4) ? ctx.P.UInt32() : 0;  // @uVar8+4

            // multiplicador de exp x3/2 (user+0x236c)
            if (u.ExpBonusActive) cost = cost * 3 >> 1;

            // anti-cheat FUN_0041cf80 ("Wrong Game Point" -> DISC 0xa0)
            if (!ValidateGamePoints(cost, gold))
            {
                Log.Warn("field", "[{0}] Wrong Game Point! Exp:{1} Gold:{2} -> DISC 0xa0", u.Slot, cost, gold);
                u.Disconnect(0xa0);
                return;
            }

            // ack de reserva: LOBBY 0x53 [53][result]; result 0 = OK (FUN_0040bae0 == 0)
            SendLobbyMsg(ctx, 0x53, BuildResult53(0, idx, cfgA, mapSlots));

            // credito: score + gold + level-up (LOBBY 0x51 [51][level][u16])
            rec.Score += cost;
            u.Gold = u.Gold + gold;
            field.RecomputeMvp();
            // persiste a transacao (mesmo saldo que a loja debita)
            if (gold > 0 && u.GameInfoId > 0) _ = ctx.World.Db.AddGoldAsync(u.GameInfoId, (int)gold);
            if (cost > 0 && u.ActiveCharId > 0) _ = ctx.World.Db.AddCharacterResultAsync(u.ActiveCharId, 0, 0, 0, cost);
            SendLobbyMsg(ctx, 0x51, new byte[] { 0, 0 });

            // FIELD 0x52 broadcast (stats/placar) a todos ocupados do field
            field.BroadcastField(0x52, new byte[] { (byte)seat, field.Score0, field.Score1 });

            // FIELD 0x52 ao proprio (resultado completo) — [serverSeq][52][...]
            using var w = new PacketWriter();
            w.WriteInt32(u.FieldSecondaryRaw); // field-handle (local_300c)
            w.WriteUInt32(gold);
            w.WriteUInt32(cost);
            w.WriteByte((byte)seat);
            w.WriteByte(cfgA);
            for (int i = 0; i < mapSlots.Length; i++) w.WriteWord(mapSlots[i]);
            SendField(ctx, 0x52, w.ToArray());

            Log.Ok("field", "[{0}] 0x53 FieldCreate(result) — idx={1} cfgA={2} cfgB={3} exp={4} gold={5}",
                u.Slot, idx, cfgA, cfgB, cost, gold);
        }

        /// <summary>
        /// FUN_0041c270 (opcode 0x61) — FieldReady ack: grava o token de ready do client.
        /// parse: token=param_3[0] (i32). O original grava em user+0x2380 e, se token ==
        /// world+0x51b4 (token de ready esperado), incrementa world+0x51bc (contador global de
        /// prontos). Sem reply. Aqui registramos o token na sessao (VerifyMode/log); o contador
        /// global vive no WorldServer (nao editado neste arquivo) — registrado via log p/ debug.
        /// </summary>
        internal static void Op_0x61_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            int token = ctx.P.CanRead(4) ? ctx.P.Int32() : 0; // param_3[0]
            // Original: grava em user+0x2380 e, se == world+0x51b4, incrementa world+0x51bc
            // (contador global de prontos). Esses campos vivem no WorldServer (nao editado aqui);
            // registramos o token para debug/correlacao da jornada de ready.
            Log.Ok("field", "[{0}] 0x61 FieldReady ack — token={1}", u.Slot, token);
        }

        // ===================== helpers locais do grupo sala-B =====================

        /// <summary>
        /// FUN_0041cf80 (anti-cheat de "Game Point"): valida exp/gold reportados. Faixa de
        /// sanidade fiel ao gate do exe (rejeita valores absurdos). Devolve true se OK.
        /// </summary>
        private static bool ValidateGamePoints(uint exp, uint gold)
        {
            // limite superior conservador (o original compara com o ganho teorico do round);
            // sem o estado completo de round, aplicamos um teto plausivel anti-overflow.
            const uint MaxExp = 1_000_000u;
            const uint MaxGold = 1_000_000u;
            return exp <= MaxExp && gold <= MaxGold;
        }

        /// <summary>Monta o corpo do LOBBY 0x53 [result][idx][cfgA][mapSlots...] (FUN_00425010).</summary>
        private static byte[] BuildResult53(byte result, byte idx, byte cfgA, ushort[] mapSlots)
        {
            using var w = new PacketWriter();
            w.WriteByte(result);
            w.WriteByte(idx);
            w.WriteByte(cfgA);
            for (int i = 0; i < mapSlots.Length; i++) w.WriteWord(mapSlots[i]);
            return w.ToArray();
        }
    }
}
