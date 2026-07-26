using System;

namespace RakionServer.World.Domain
{
    [Flags]
    public enum BotControls : byte
    {
        None = 0,
        W = 1 << 0,
        A = 1 << 1,
        S = 1 << 2,
        D = 1 << 3,
        Space = 1 << 4,
        Attack = 1 << 5
    }

    public enum BotNavigationMode : byte
    {
        Idle,
        Approach,
        Attack,
        Backward,
        BypassDiagonal,
        BypassStrafe,
        Backstep
    }

    public readonly record struct BotNavigationAction(
        BotControls Controls,
        BotNavigationMode Mode)
    {
        public bool IsMoving =>
            (Controls & (BotControls.W | BotControls.A |
                         BotControls.S | BotControls.D)) != 0;

        public bool IsAttacking => (Controls & BotControls.Attack) != 0;
        public bool IsJumping => (Controls & BotControls.Space) != 0;
    }

    public readonly record struct BotNavigationInput(
        long NowMs,
        byte TargetSeat,
        BotVector Position,
        BotVector Target,
        float AttackRange,
        float MinimumSpacing,
        bool PreferLeft);
}
