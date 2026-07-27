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
}
