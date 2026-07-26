using RakionServer.World.Domain;

namespace RakionServer.World.Navigation
{
    public sealed class BattleMapNavigationSurface : IBotNavigationSurface
    {
        public const byte CageMapId = 209;
        public static BattleMapNavigationSurface Instance { get; } = new();

        private const float CageBarrierMaximumX = 3900f;
        private const float CageBarrierMinimumZ = -1250f;
        private const float CageBarrierMaximumZ = -550f;

        private BattleMapNavigationSurface()
        {
        }

        public BotMoveResolution Resolve(
            byte mapId,
            BotVector current,
            BotVector proposed)
        {
            if (mapId != CageMapId || proposed.X > CageBarrierMaximumX)
                return new BotMoveResolution(proposed, false);
            return ResolveCageBarrier(current, proposed);
        }

        private static BotMoveResolution ResolveCageBarrier(
            BotVector current,
            BotVector proposed)
        {
            if (current.Z >= CageBarrierMaximumZ &&
                proposed.Z < CageBarrierMaximumZ)
            {
                BotVector constrained = proposed with { Z = CageBarrierMaximumZ };
                return new BotMoveResolution(constrained, true);
            }
            if (current.Z <= CageBarrierMinimumZ &&
                proposed.Z > CageBarrierMinimumZ)
            {
                BotVector constrained = proposed with { Z = CageBarrierMinimumZ };
                return new BotMoveResolution(constrained, true);
            }
            if (proposed.Z > CageBarrierMinimumZ &&
                proposed.Z < CageBarrierMaximumZ)
            {
                float z = current.Z >= CageBarrierMaximumZ
                    ? CageBarrierMaximumZ
                    : CageBarrierMinimumZ;
                return new BotMoveResolution(proposed with { Z = z }, true);
            }
            return new BotMoveResolution(proposed, false);
        }
    }
}
