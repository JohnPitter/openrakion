using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World;

public sealed record StageResultReport(
    byte StageId, byte Rank, IReadOnlyList<ushort> MapSlots,
    uint ReportedExp, uint ReportedGold,
    uint CellExpSlot1, uint CellExpSlot2, uint CellExpSlot3);

public sealed partial class WorldServer
{
    private StageCatalog _stageCatalog = new(Array.Empty<StageDefinition>());

    public int StageCatalogCount => _stageCatalog.Count;

    public ushort? StageDurationSeconds(byte stageId) =>
        _stageCatalog.TryGetContent(stageId, out StageContentDefinition? content)
            ? content.TimeLimitSeconds
            : null;

    internal StageReward? CalculateStageReward(byte stageId, byte rank, byte previousBestRank) =>
        _stageCatalog.TryGetContent(stageId, out StageContentDefinition? content)
            ? StageRewardPolicy.Calculate(content, rank, previousBestRank)
            : null;

    public bool BeginStageRun(ClientSession session, Field field)
    {
        if (field.Mode != 0 || field.MatchId == Guid.Empty) return false;
        bool levelFree = StageLevelFreePolicy.IsActive(
            session.StageLevelFreeMarker, DateTime.Now);
        if (!_stageCatalog.CanEnter(field.MapId, session.CharLevel, field.Count, levelFree))
        {
            Log.Warn("stage", "[{0}] entrada negada: stage={1} level={2} players={3} levelFree={4}",
                session.Slot, field.MapId, session.CharLevel, field.Count, levelFree);
            session.FinishStageRun();
            return false;
        }
        if (!_stageCatalog.TryGetContent(field.MapId, out StageContentDefinition? content))
        {
            Log.Warn("stage", "[{0}] conteúdo versionado ausente: stage={1}",
                session.Slot, field.MapId);
            session.FinishStageRun();
            return false;
        }
        if (session.StageRanks is null || field.MapId >= session.StageRanks.Length)
        {
            Log.Warn("stage", "[{0}] ranks não carregados: stage={1}",
                session.Slot, field.MapId);
            session.FinishStageRun();
            return false;
        }
        field.RoundDurationSec = content.TimeLimitSeconds;
        byte previousBestRank = session.StageRanks[field.MapId];
        session.BeginStageRun(field.MatchId, field.MapId, previousBestRank);
        Log.Ok("stage", "[{0}] run {1} iniciado: stage={2} players={3} duration={4}s goal={5}",
            session.Slot, field.MatchId.ToString("N"), field.MapId, field.Count,
            content.TimeLimitSeconds, content.Goal);
        return true;
    }

    public IReadOnlyList<ClientSession>? MarkStageClear(ClientSession session)
    {
        Field? field = GetField(session.FieldId);
        if (field == null || field.Mode != 0 || field.MatchId != session.StageRunId ||
            field.MapId != session.ActiveStageId)
            return null;
        lock (field.SyncRoot)
        {
            PlayerRec? sender = field.FindRec(session);
            if (sender == null || sender.Slot != field.MasterSlot) return null;

            var participants = new List<ClientSession>();
            foreach (PlayerRec record in field.Slots)
            {
                ClientSession? participant = record.Session;
                if (!record.Playing || participant == null) continue;
                if (participant.StageRunId != field.MatchId ||
                    participant.ActiveStageId != field.MapId)
                    return null;
                participants.Add(participant);
            }
            if (participants.Count == 0 || !field.ApplyStageClear(sender.Slot)) return null;
            foreach (ClientSession participant in participants)
                participant.MarkStageRunCleared();
            return participants;
        }
    }

    public async Task<StageSettlementResult> ApplyStageResultAsync(
        ClientSession session, StageResultReport report)
    {
        CharacterProgressionState current = CurrentProgression(session);
        if (!CanSettleStage(session, report))
            return FailedStageResult(current);

        var gate = _characterLocks.GetOrAdd(
            session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            uint appliedExp = session.BonusExp(report.ReportedExp);
            uint appliedGold = session.BonusGold(report.ReportedGold);
            uint[] cellExp =
                [report.CellExpSlot1, report.CellExpSlot2, report.CellExpSlot3];
            IReadOnlyList<CellProgressionChange> cells = session.StageRunCellChanges ??
                ProjectCells(session, cellExp, uint.MaxValue);
            CharacterProgressionState before = CurrentProgression(session);
            CharacterProgressionState after = CharacterProgression.Project(
                before, appliedExp, level => NextLevelExp(session.CharClass, level));
            uint goldAfter = checked(session.Gold + appliedGold);
            var identity = new StageRunIdentity(
                session.StageRunId, session.ActiveCharId,
                session.GameInfoId, session.ActiveStageId);
            var award = new StageResultAward(
                report.Rank, report.ReportedExp, report.ReportedGold,
                report.CellExpSlot1, report.CellExpSlot2, report.CellExpSlot3,
                appliedExp, appliedGold);
            StageSettlementResult result = await _db.SettleStageResultAsync(
                new StageSettlementRequest(identity, award, before, after, session.Gold, goldAfter)
                {
                    Cells = cells
                });
            if (result.Status == StageSettlementStatus.Failed) return result;

            session.CharExp = result.Progression.Exp;
            session.CharLevel = result.Progression.Level;
            session.CharLevelPoint = result.Progression.LevelPoint;
            session.Gold = result.Gold;
            if (result.Status == StageSettlementStatus.Applied)
            {
                session.StageRunCellChanges = cells;
                session.ApplyCellProgression(cells);
            }
            if (session.StageRanks != null && report.StageId < session.StageRanks.Length)
                session.StageRanks[report.StageId] = result.BestRank;
            Log.Ok("stage", "[{0}] run {1} {2}: stage={3} rank={4} exp=+{5} gold=+{6}",
                session.Slot, session.StageRunId.ToString("N"), result.Status,
                report.StageId, result.BestRank, appliedExp, appliedGold);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("stage", "[{0}] settlement falhou: {1}", session.Slot, ex.Message);
            return FailedStageResult(CurrentProgression(session));
        }
        finally
        {
            gate.Release();
        }
    }

    private bool CanSettleStage(ClientSession session, StageResultReport report)
    {
        Field? field = GetField(session.FieldId);
        if (field == null || field.Mode != 0 || field.Phase != MatchPhase.RoundEnd ||
            !_stageCatalog.TryGetContent(report.StageId, out StageContentDefinition? content))
            return false;
        if (session.StageRunId == Guid.Empty || !session.StageRunCleared ||
            field.MatchId != session.StageRunId || report.StageId != session.ActiveStageId ||
            field.MapId != report.StageId || report.Rank > 5 || report.MapSlots.Count >= 5 ||
            !GamePointRules.IsValid(0, field.Round, field.MaxRounds,
                report.ReportedExp, report.ReportedGold))
            return false;

        StageReward expected = StageRewardPolicy.Calculate(
            content, report.Rank, session.StageRunPreviousBestRank);
        if (report.ReportedExp != expected.Exp || report.ReportedGold != expected.Gold)
            return false;

        IReadOnlyList<EquippedCellState> cells = session.GetEquippedCellStates();
        uint[] reportedCells =
            [report.CellExpSlot1, report.CellExpSlot2, report.CellExpSlot3];
        for (int index = 0; index < reportedCells.Length; index++)
        {
            bool equipped = cells[index].RowId > 0 && cells[index].ItemId is >= 8000 and < 9000;
            if (reportedCells[index] != StageRewardPolicy.CellExp(
                    expected.Exp, equipped, session.ExpBonusActive))
                return false;
        }
        foreach (ushort slot in report.MapSlots)
            if (slot >= MaxUser) return false;
        return true;
    }

    private static StageSettlementResult FailedStageResult(CharacterProgressionState state) =>
        new(StageSettlementStatus.Failed, state.Level, state, 0, 0);
}
