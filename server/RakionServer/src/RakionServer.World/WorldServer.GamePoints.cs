using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World;

public readonly record struct GamePointTarget(Field Field, PlayerRec Player, byte Seat);

public sealed record CompetitiveGamePoint(
    GamePointIdentity Identity, GamePointTarget Target, GamePointAward Award,
    IReadOnlyList<uint> CellExp)
{
    public Field Field => Target.Field;
    public PlayerRec Player => Target.Player;
    public byte Seat => Target.Seat;
    public uint Exp => Award.Exp;
    public uint Gold => Award.Gold;
}

public sealed partial class WorldServer
{
    private System.Collections.Generic.Dictionary<(int Npc, byte Level), long> _cellLevelCurve = new();
    private long _cellLevel99Cap;

    private long? CellLevelExp(int npc, byte level) =>
        _cellLevelCurve.TryGetValue((npc, level), out long exp) ? exp : null;

    public async Task<GamePointSettlementResult> ApplyCompetitiveGamePointAsync(
        ClientSession session, CompetitiveGamePoint gamePoint)
    {
        if (!MatchesSession(session, gamePoint))
            return FailedGamePoint(session);
        if (gamePoint.CellExp.Count != 3) return FailedGamePoint(session);

        var gate = _characterLocks.GetOrAdd(
            session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            CharacterProgressionState before = CurrentProgression(session);
            CharacterProgressionState after = CharacterProgression.Project(
                before, gamePoint.Exp, level => NextLevelExp(session.CharClass, level));
            uint nextGold = checked(session.Gold + gamePoint.Gold);
            uint nextResultPoints = checked(gamePoint.Player.ResultPoints + gamePoint.Exp);
            IReadOnlyList<CellProgressionChange> cells = ProjectCells(session, gamePoint.CellExp);
            var request = new GamePointSettlementRequest(
                gamePoint.Identity, gamePoint.Award, before, after) { Cells = cells };
            GamePointSettlementResult result = await _db.SettleGamePointAsync(request);
            if (result.Status != GamePointSettlementStatus.Applied) return result;

            session.CharExp = after.Exp;
            session.CharLevel = after.Level;
            session.CharLevelPoint = after.LevelPoint;
            session.Gold = nextGold;
            session.ApplyCellProgression(cells);
            lock (gamePoint.Field.SyncRoot)
            {
                gamePoint.Player.ResultPoints = nextResultPoints;
            }
            if (result.LevelUps > 0)
                Log.Ok("level", "[{0}] char {1} LEVEL UP -> {2} (+{3}, exp total {4})",
                    session.Slot, session.ActiveCharId, after.Level, result.LevelUps, after.Exp);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("field", "[{0}] falha ao aplicar game point: {1}", session.Slot, ex.Message);
            return FailedGamePoint(session);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool MatchesSession(ClientSession session, CompetitiveGamePoint gamePoint) =>
        gamePoint.Identity.CharacterId == session.ActiveCharId &&
        gamePoint.Identity.GameInfoId == session.GameInfoId &&
        gamePoint.Identity.MatchId != Guid.Empty && gamePoint.Identity.Round > 0;

    private static CharacterProgressionState CurrentProgression(ClientSession session) =>
        new(session.CharExp, session.CharLevel, (byte)Math.Min(session.CharLevelPoint, byte.MaxValue));

    private IReadOnlyList<CellProgressionChange> ProjectCells(
        ClientSession session, IReadOnlyList<uint> reportedExp,
        uint maxAppliedExp = 100)
    {
        IReadOnlyList<EquippedCellState> current = session.GetEquippedCellStates();
        var result = new CellProgressionChange[3];
        for (int index = 0; index < result.Length; index++)
            result[index] = CellProgression.Project(
                current[index], reportedExp[index], CellLevelExp,
                _cellLevel99Cap, maxAppliedExp);
        return result;
    }

    private static GamePointSettlementResult FailedGamePoint(ClientSession session)
    {
        CharacterProgressionState current = CurrentProgression(session);
        return new GamePointSettlementResult(
            GamePointSettlementStatus.Failed, current.Level, current);
    }
}
