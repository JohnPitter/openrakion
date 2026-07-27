using System;

namespace RakionServer.World.Domain;

public readonly record struct PlayerDamageResult(
    int Damage,
    int RemainingHp,
    int RemainingAp,
    bool Died);

public sealed class PlayerCombatVitals
{
    public int MaxHp { get; private set; }
    public int MaxAp { get; private set; }
    public int Hp { get; private set; }
    public int Ap { get; private set; }
    public long RespawnAtMs { get; private set; }
    public byte LastDamageSourceSeat { get; private set; } = Field.NoSeat;
    public bool Initialized => MaxHp > 0;
    public bool Alive => Initialized && Hp > 0;

    public void Initialize(int maxHp, int maxAp)
    {
        if (maxHp <= 0 || maxAp < 0)
            throw new ArgumentOutOfRangeException(nameof(maxHp));
        MaxHp = maxHp;
        MaxAp = maxAp;
        Hp = maxHp;
        Ap = maxAp;
        RespawnAtMs = 0;
        LastDamageSourceSeat = Field.NoSeat;
    }

    public PlayerDamageResult ApplyDamage(int damage, byte sourceSeat)
    {
        if (!Alive || damage <= 0 || sourceSeat >= Field.NoSeat)
            return default;
        LastDamageSourceSeat = sourceSeat;
        int absorbed = Math.Min(Ap, damage);
        Ap -= absorbed;
        Hp = Math.Max(0, Hp - (damage - absorbed));
        return new PlayerDamageResult(damage, Hp, Ap, Hp == 0);
    }

    public void ScheduleRespawn(long nowMs, int delayMs)
    {
        RespawnAtMs = !Alive && delayMs > 0
            ? nowMs + delayMs
            : 0;
    }

    public bool TryRespawn(long nowMs)
    {
        if (Alive || RespawnAtMs == 0 || nowMs < RespawnAtMs)
            return false;
        Hp = MaxHp;
        Ap = MaxAp;
        RespawnAtMs = 0;
        return true;
    }

    public void Reset()
    {
        MaxHp = 0;
        MaxAp = 0;
        Hp = 0;
        Ap = 0;
        RespawnAtMs = 0;
        LastDamageSourceSeat = Field.NoSeat;
    }
}
