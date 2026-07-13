using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Progressão do personagem: curva de exp (classlevelinfo), crédito de exp/gold, level-ups e resultado de
    /// stage. Regra de NEGÓCIO extraída do <see cref="WorldServer"/> — opera sobre a <see cref="ClientSession"/>
    /// (que aplica os bônus de PU internamente) e persiste via <see cref="WorldDatabase"/>. A curva é carregada
    /// UMA vez no boot (<see cref="LoadCurveAsync"/>).
    /// </summary>
    public sealed class ProgressionService
    {
        private readonly WorldDatabase _db;
        private Dictionary<(byte Cls, byte Level), int> _levelCurve = new();

        public ProgressionService(WorldDatabase db) => _db = db;

        /// <summary>Carrega a curva de exp por classe (classlevelinfo). Chamado no boot (WorldServer.StartAsync).</summary>
        public async Task LoadCurveAsync()
        {
            _levelCurve = await _db.LoadLevelCurveAsync();
            Log.Ok("level", "curva de level carregada: {0} entradas (classlevelinfo)", _levelCurve.Count);
        }

        /// <summary>Exp TOTAL p/ avancar do nivel atual (classlevelinfo). 0 = sem proximo nivel.</summary>
        public int NextLevelExp(byte cls, byte level) => _levelCurve.TryGetValue((cls, level), out var e) ? e : 0;

        /// <summary>
        /// Credita exp ao char ativo e processa level-ups (FUN_0040d300): acumula CharExp,
        /// sobe CharLevel/CharLevelPoint e persiste exp + nivel no characterinfo. Devolve quantos niveis subiu.
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
