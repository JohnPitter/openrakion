namespace RakionServer.World.Domain;

public readonly record struct BotAttackAnimationStep(
    byte AnimationId,
    int OffsetMs);

public sealed class BotAttackPresentation
{
    private const byte CapturedArcherClass = 1;

    private static readonly BotAttackAnimationStep[][] ArcherProfiles =
    [
        [
            new(25, 0),
            new(24, 547),
            new(12, 703)
        ],
        [
            new(27, 0),
            new(26, 297),
            new(18, 407)
        ],
        [
            new(0, 0),
            new(1, 554)
        ]
    ];

    private static readonly BotAttackAnimationStep[][] ConservativeProfiles =
    [
        [new(25, 0)],
        [new(24, 0)],
        [new(12, 0)]
    ];

    private BotAttackAnimationStep[] _steps = [];
    private long _startedAtMs;
    private int _nextStep;

    public void Start(
        byte charClass,
        BotAttackVariant variant,
        long nowMs)
    {
        BotAttackAnimationStep[][] profiles = charClass == CapturedArcherClass
            ? ArcherProfiles
            : ConservativeProfiles;
        _steps = profiles[(int)variant];
        _startedAtMs = nowMs;
        _nextStep = 0;
    }

    public bool TryTake(long nowMs, out byte animationId)
    {
        animationId = 0;
        if (_nextStep >= _steps.Length)
            return false;
        BotAttackAnimationStep step = _steps[_nextStep];
        if (nowMs - _startedAtMs < step.OffsetMs)
            return false;
        animationId = step.AnimationId;
        _nextStep++;
        return true;
    }

    public void Reset()
    {
        _steps = [];
        _startedAtMs = 0;
        _nextStep = 0;
    }
}
