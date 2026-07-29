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
    private bool _attackAnimationHeld;
    private byte _heldAttackAnimation;

    public uint ConfirmedHitSequence { get; private set; }

    public bool TryOpenAttack(uint sequence, long nowMs) =>
        TryOpenAttack(sequence, nowMs, ImpactDelayMs, ActiveDurationMs);

    /// <summary>
    /// Golpe é a BORDA de subida da animação de ataque. Segurar o botão faz o cliente repetir a
    /// mesma animação com sequência crescente; sem essa regra cada repetição virava um acerto e o
    /// alvo morria durante o carregamento, sem o golpe sair. Troca de animação conta como novo
    /// golpe, preservando combo.
    /// </summary>
    public bool TryOpenAttack(uint sequence, long nowMs, byte animationId)
    {
        if (_attackAnimationHeld && animationId == _heldAttackAnimation)
            return false;
        _attackAnimationHeld = true;
        _heldAttackAnimation = animationId;
        return TryOpenAttack(sequence, nowMs);
    }

    /// <summary>Qualquer animação que não seja ataque encerra o golpe em curso.</summary>
    public void ReleaseAttackAnimation() => _attackAnimationHeld = false;

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
