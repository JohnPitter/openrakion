namespace RakionServer.World.Domain;

public readonly record struct PlayerAttackWindow(
    uint Sequence,
    long OpensAtMs,
    long ClosesAtMs);

public sealed class PlayerCombatState
{
    private const int ImpactDelayMs = 120;
    private const int ActiveDurationMs = 330;
    private const int MinimumAttackIntervalMs = 250;

    private bool _hasAcceptedSequence;
    private uint _lastAcceptedSequence;
    private long _nextAttackAtMs;
    private PlayerAttackWindow? _pendingAttack;

    public uint ConfirmedHitSequence { get; private set; }

    public bool TryOpenAttack(uint sequence, long nowMs) =>
        TryOpenAttack(sequence, nowMs, ImpactDelayMs, ActiveDurationMs);

    /// <summary>
    /// Abre janela com atraso/duração explícitos. Bots usam impacto imediato porque o Host
    /// nativo e o resolve autoritativo rodam no mesmo tick do World.
    /// </summary>
    public bool TryOpenAttack(
        uint sequence,
        long nowMs,
        int impactDelayMs,
        int activeDurationMs)
    {
        if (!IsNewSequence(sequence))
            return false;
        _hasAcceptedSequence = true;
        _lastAcceptedSequence = sequence;
        if (nowMs < _nextAttackAtMs)
            return false;
        if (impactDelayMs < 0 || activeDurationMs <= 0)
            return false;

        _nextAttackAtMs = nowMs + MinimumAttackIntervalMs;
        _pendingAttack = new PlayerAttackWindow(
            sequence,
            nowMs + impactDelayMs,
            nowMs + impactDelayMs + activeDurationMs);
        return true;
    }

    public bool TryGetActiveAttack(long nowMs, out PlayerAttackWindow attack)
    {
        attack = default;
        if (_pendingAttack is not { } pending)
            return false;
        if (nowMs > pending.ClosesAtMs)
        {
            _pendingAttack = null;
            return false;
        }
        if (nowMs < pending.OpensAtMs)
            return false;
        attack = pending;
        return true;
    }

    public uint ConfirmHit(uint attackSequence)
    {
        if (_pendingAttack?.Sequence != attackSequence)
            return 0;
        _pendingAttack = null;
        return ++ConfirmedHitSequence;
    }

    public void Reset()
    {
        _hasAcceptedSequence = false;
        _lastAcceptedSequence = 0;
        _nextAttackAtMs = 0;
        _pendingAttack = null;
        ConfirmedHitSequence = 0;
    }

    private bool IsNewSequence(uint sequence) =>
        !_hasAcceptedSequence ||
        unchecked((int)(sequence - _lastAcceptedSequence)) > 0;
}
