namespace RakionServer.World.Domain
{
    public readonly record struct BotMoveResolution(
        BotVector Position,
        bool Blocked);

    public interface IBotNavigationSurface
    {
        BotMoveResolution Resolve(
            byte mapId,
            BotVector current,
            BotVector proposed);
    }
}
