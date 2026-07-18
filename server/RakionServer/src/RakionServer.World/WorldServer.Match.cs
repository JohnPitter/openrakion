using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>Motor de partida (FieldEngine/GameClock/MatchTick/Settle) + progressao de exp/level.
    /// Regra de dominio do match — os handlers de rede so traduzem bytes e chamam aqui.</summary>
    public sealed partial class WorldServer
    {
        /// <summary>
        /// Motor da partida por-field (FUN_00409940 + FUN_004069a0): roda a maquina de estado
        /// de cada field ativo (Pre->Playing->RoundEnd->proximo-round/fim) e dispara os
        /// broadcasts (0x48/0x49/0x4a/0x44). Roda em loop unico (~200ms), NAO por-sessao.
        /// Tambem mantem o tick 1583 (UDP) idle a cada ~150ms p/ os players in-field.
        /// </summary>
        private async Task FieldEngineLoopAsync(CancellationToken ct)
        {
            Log.Ok("field", "motor da partida (FieldEngine) iniciado");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Domain.Field[] snapshot;
                    lock (Fields) snapshot = Fields.ToArray();
                    foreach (var f in snapshot)
                    {
                        if (f.State == 2) MatchTick(f);
                        else if (f.State == 1 && !f.Settled) SettleMatch(f);
                    }

                }
                catch (Exception ex) { Log.Debug("field", "engine tick: {0}", ex.Message); }
                await Task.Delay(100, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Relogio de gameplay 1583 (150ms) APENAS p/ salas BATTLE/PvP (Mode != 0): GameSeq
        /// INCREMENTA a cada tick — e' o frame/clock da partida; seq fixo congela o personagem e
        /// cadencia errada deixa o cliente congelado ate o seq alinhar (~2min observados a 200ms).
        /// Solo stage (Mode 0) e' client-side: sem tick (eco/timer no solo interrompia combos).
        /// Loop dedicado p/ manter os 150ms (o engine loop dorme 100ms -> tick efetivo de 200ms).
        /// </summary>
        private async Task GameClockLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Domain.Field[] snapshot;
                    lock (Fields) snapshot = Fields.ToArray();
                    foreach (var f in snapshot)
                    {
                        if (f.State != 2) continue;   // solo E PvP — sem o clock o cliente solo nao manda input (trava no briefing)
                        foreach (var r in f.Slots)
                        {
                            var s = r.Session;
                            if (s == null || !r.Occupied || s.UdpEndpoint == null) continue;
                            unchecked { s.GameSeq++; }
                            _udpGame?.SendTick(s.UdpEndpoint, s.GameSeq);
                        }
                    }
                }
                catch (Exception ex) { Log.Debug("field", "game clock: {0}", ex.Message); }
                await Task.Delay(150, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        // escrito pelo engine loop E pelos handlers de sessao (NotifyPlayerReady) -> concurrent
        private readonly ConcurrentDictionary<int, long> _fieldStatusBeat = new();

        /// <summary>
        /// Um tick do motor de um field (FUN_00409940). Avanca as fases pelo deadline (field+0x2b8),
        /// re-broadcasta 0x48 (FieldStatus) a cada ~1s, e dispara fim-de-round / fim-de-match.
        /// </summary>
        private void MatchTick(Domain.Field f)
        {
            long now = Environment.TickCount64;

            switch (f.Phase)
            {
                case Domain.MatchPhase.Playing:
                    // SOLO PvE (Mode 0, time-attack Stage Clear): combate + countdown + clear sao
                    // CLIENT-SIDE. NAO re-enviar 0x48 (re-envio glitchava o countdown 3->1 e
                    // interrompia combos) e NAO rodar logica de round/placar; o cliente conduz.
                    if (f.Mode == 0) break;

                    // PvP (GOLEM/DEATHMATCH/TEAMDEATH/BOSS): motor de round servidor-side.
                    // SEM re-broadcast periodico de 0x48: o re-envio interrompia combos no solo e a
                    // captura do room flow (mitm_full_113423) mostra UM 0x48 so na entrada. O cliente
                    // conta o tempo sozinho a partir dele; cadencia real em PvP = pendente de captura.
                    if (f.Warned30 == 0 && f.RemainingSec() <= 30)
                    {
                        f.Warned30 = 1; // field+0x2be (flag aviso de 30s)
                        Log.Info("field", "field {0} round {1}: 30s restantes", f.Id, f.Round);
                    }
                    // tempo esgotado -> fim de round por placar (FUN_00409940 deadline)
                    if (now >= f.DeadlineMs)
                    {
                        f.EndRound(f.DecideRoundWinnerByScore());
                        // FIELD 0x4a aos playing: body=[cause/2bd][2bf][2c0][2c1] (mesmo layout dos
                        // handlers 0x4a/0x4d de fim-de-round)
                        f.BroadcastFieldPlaying(0x4a,
                            new byte[] { f.LastRoundWinner, f.WinnerSide, f.Wins0, f.Wins1 });
                    }
                    break;

                case Domain.MatchPhase.RoundEnd:
                    if (now >= f.DeadlineMs)
                    {
                        f.Round++;
                        if (f.Round > f.MaxRounds)
                        {
                            f.EndMatch(2); // acabaram os rounds (empate em rounds)
                            f.BroadcastLobby(f.BuildMatchEnd(2));
                            _fieldStatusBeat.TryRemove(f.Id, out _);
                        }
                        else if (f.CountPlaying() == 0)
                        {
                            f.EndMatch(5); // sem jogadores
                            f.BroadcastLobby(f.BuildMatchEnd(5));
                            _fieldStatusBeat.TryRemove(f.Id, out _);
                        }
                        else
                        {
                            // PROXIMO ROUND: reinicia o relogio/golens e anuncia (0x49 NovoRound + 0x48).
                            f.StartRound();
                            f.BroadcastLobby(f.Build0x49());
                            f.BroadcastLobby(f.Build0x48());
                            Log.Ok("field", "field {0} -> round {1}/{2} (w0={3} w1={4})", f.Id, f.Round, f.MaxRounds, f.Wins0, f.Wins1);
                        }
                    }
                    break;

                case Domain.MatchPhase.Pre:
                default:
                    break;
            }
        }

        /// <summary>
        /// Liquida o resultado do MATCH no DB (roda 1x apos EndMatch, field+8==1): incrementa
        /// win/lose/draw do characterinfo de cada jogador conforme o time vencedor (Wins0 vs
        /// Wins1; empate = draw p/ todos) e atualiza o overlay em memoria (CharWin/Lose/Draw).
        /// Mode 0 (solo PvE) nao liquida — o resultado vem do cliente pelos 0x50/0x53.
        /// </summary>
        private void SettleMatch(Domain.Field f)
        {
            f.Settled = true;
            if (f.Mode == 0) return;
            byte winner = f.Wins0 > f.Wins1 ? (byte)0 : f.Wins1 > f.Wins0 ? (byte)1 : (byte)2;
            foreach (var r in f.Slots)
            {
                var s = r.Session;
                if (s == null || !r.Occupied || s.ActiveCharId <= 0) continue;
                int win = 0, lose = 0, draw = 0;
                if (winner == 2) draw = 1;
                else if (r.Team == winner) win = 1;
                else lose = 1;
                s.CharWin += (uint)win; s.CharLose += (uint)lose; s.CharDraw += (uint)draw;
                _ = _db.AddCharacterResultAsync(s.ActiveCharId, win, lose, draw, exp: 0);
                Log.Ok("field", "field {0} settle: char {1} seat {2} -> {3} (score {4})",
                    f.Id, s.ActiveCharId, r.Slot, win != 0 ? "WIN" : lose != 0 ? "LOSE" : "DRAW", r.Score);
            }
        }

        /// <summary>
        /// Dispara o 0x48 FieldStatus de inicio (handler 0x48 / FUN_00408440): o player marcou ready.
        /// Se a partida (re)iniciou, broadcasta o 0x48 a todos. Usado pelos handlers de campo.
        /// </summary>
        public void NotifyPlayerReady(Domain.Field f, ClientSession s)
        {
            // Time-attack solo: cada entrada no stage RECOMECA o cronometro do 0:00. Reseta o DeadlineMs
            // (RemainingSec volta ao cheio = 603); senao um field reaproveitado (Phase ja Playing -> StartRound
            // nao roda) deixaria o DeadlineMs obsoleto e o HUD comecaria fora do zero.
            f.DeadlineMs = Environment.TickCount64 + (f.RoundDurationSec + 3) * 1000L;
            bool started = f.OnPlayerReady(s);
            if (started)
            {
                _fieldStatusBeat[f.Id] = Environment.TickCount64;
                f.BroadcastLobby(f.Build0x48());
                Log.Ok("field", "[{0}] partida iniciada no field {1} (0x48 a {2} player(s))", s.Slot, f.Id, f.CountPlaying());
            }
            else
            {
                // spawn tardio / aguardando os demais: 0x48 so a este player
                try { s.SendEncryptedFrame(f.Build0x48()); } catch { }
            }
        }

        private System.Collections.Generic.Dictionary<(byte Cls, byte Level), int> _levelCurve = new();

        /// <summary>Exp TOTAL p/ avancar do nivel atual (classlevelinfo). 0 = sem proximo nivel.</summary>
        public int NextLevelExp(byte cls, byte level) => _levelCurve.TryGetValue((cls, level), out var e) ? e : 0;

        /// <summary>
        /// Credita exp ao char ativo e processa level-ups (FUN_0040d300): acumula CharExp,
        /// sobe CharLevel/CharLevelPoint e persiste exp + nivel no characterinfo. O threshold de cada
        /// nivel e' o MEIO do intervalo da curva classlevelinfo ((curva[L-1]+curva[L])/2) — house-rule
        /// p/ "barra cheia = upa" exato (o cliente desenha o cheio no meio do span; ver loop).
        /// Devolve quantos niveis subiu (0 = nenhum).
        /// </summary>
        public int GrantExp(ClientSession s, uint exp)
        {
            if (s.ActiveCharId <= 0 || exp == 0) return 0;
            s.CharExp += exp;
            _ = _db.AddCharacterResultAsync(s.ActiveCharId, 0, 0, 0, exp);
            return SettleLevels(s);
        }

        /// <summary>Aplica o RESULTADO de um stage solo (handler 0x53): bônus de PU sobre exp/gold, credita gold
        /// (saldo + DB), grava o MELHOR rank do stage (userstageinfo) e concede exp (curva classlevelinfo).
        /// Devolve o nº de level-ups. Regra de negócio do motor de partida/progressão — fora do handler de rede.</summary>
        public int ApplyStageResult(ClientSession s, byte stage, byte rank, uint exp, uint gold)
        {
            exp = s.BonusExp(exp); gold = s.BonusGold(gold);                       // bônus de PU (pu_config)
            s.Gold += gold;
            if (gold > 0 && s.GameInfoId > 0) _ = _db.AddGoldAsync(s.GameInfoId, (int)gold);
            if (rank > 0 && s.ActiveCharId > 0) _ = _db.SaveStageRankAsync(s.ActiveCharId, stage, rank); // melhor rank por stage
            return GrantExp(s, exp);
        }

        /// <summary>Sobe os niveis PENDENTES pela curva (sem creditar exp) e persiste. Chamado ao GANHAR exp
        /// (<see cref="GrantExp"/>) E no LOAD do char — um char carregado JÁ acima do limiar upa na hora, sem
        /// precisar ganhar exp de novo. Devolve quantos niveis subiu (0 = nenhum).</summary>
        public int SettleLevels(ClientSession s)
        {
            if (s.ActiveCharId <= 0) return 0;
            int ups = 0;
            while (s.CharLevel < 99)
            {
                int full = NextLevelExp(s.CharClass, s.CharLevel);              // curva ORIGINAL: exp do proximo nivel (curva[L])
                if (full <= 0) break;                                           // teto da curva (sem proximo nivel)
                int floor = NextLevelExp(s.CharClass, (byte)(s.CharLevel - 1)); // exp do nivel atual (curva[L-1])
                // HOUSE-RULE (desvio consciente; o DB fica intacto): o cliente desenha a barra "cheia" a ~2/5 do
                // span do nivel (display nv4 = 496 ~ 386 + (658-386)*2/5 = 494). O meio-a-meio antigo (522)
                // deixava a barra visualmente cheia SEM upar. Subimos nesse ponto p/ "barra cheia = upa".
                int next = floor + (full - floor) * 2 / 5;
                if (s.CharExp < next) break;
                s.CharLevel++;
                s.CharLevelPoint++;
                ups++;
            }
            if (ups > 0)
            {
                _ = _db.UpdateCharacterLevelAsync(s.ActiveCharId, s.CharLevel, (byte)Math.Min(s.CharLevelPoint, 255));
                Log.Ok("level", "[{0}] char {1} LEVEL UP -> {2} (+{3} nivel(is), exp total {4})",
                    s.Slot, s.ActiveCharId, s.CharLevel, ups, s.CharExp);
            }
            return ups;
        }
    }
}
