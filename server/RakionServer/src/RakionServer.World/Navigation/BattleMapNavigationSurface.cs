using RakionServer.World.Domain;

namespace RakionServer.World.Navigation
{
    public sealed class BattleMapNavigationSurface : IBotNavigationSurface
    {
        public const byte CageMapId = 209;
        public static BattleMapNavigationSurface Instance { get; } = new();

        private const float BotRadius = 80f;
        private const float CageObstacleMinimumX = 1950f - BotRadius;
        private const float CageObstacleMaximumX = 3050f + BotRadius;
        private const float CageObstacleMinimumZ = -1250f - BotRadius;
        private const float CageObstacleMaximumZ = -550f + BotRadius;

        private BattleMapNavigationSurface()
        {
        }

        public BotMoveResolution Resolve(
            byte mapId,
            BotVector current,
            BotVector proposed)
        {
            if (mapId != CageMapId)
                return new BotMoveResolution(proposed, false);
            return ResolveCageObstacle(current, proposed);
        }

        private static BotMoveResolution ResolveCageObstacle(
            BotVector current,
            BotVector proposed)
        {
            float x = ResolveAxis(
                current.X,
                proposed.X,
                current.Z,
                CageObstacleMinimumX,
                CageObstacleMaximumX,
                CageObstacleMinimumZ,
                CageObstacleMaximumZ);
            float z = ResolveAxis(
                current.Z,
                proposed.Z,
                x,
                CageObstacleMinimumZ,
                CageObstacleMaximumZ,
                CageObstacleMinimumX,
                CageObstacleMaximumX);
            var resolved = proposed with { X = x, Z = z };
            return new BotMoveResolution(
                resolved,
                resolved.X != proposed.X || resolved.Z != proposed.Z);
        }

        private static float ResolveAxis(
            float current,
            float proposed,
            float other,
            float minimum,
            float maximum,
            float otherMinimum,
            float otherMaximum)
        {
            if (other <= otherMinimum || other >= otherMaximum)
                return proposed;
            if (current <= minimum && proposed > minimum)
                return minimum;
            if (current >= maximum && proposed < maximum)
                return maximum;
            if (current > minimum && current < maximum)
                return proposed >= current ? maximum : minimum;
            return proposed;
        }
    }
}
