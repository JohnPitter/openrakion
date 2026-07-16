namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void BeginInventoryMove(byte[] data) => _ = HandleInventoryMoveAsync(data);
    }
}
