namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void BeginInventoryBuy(byte[] data) => _ = HandleStoragePurchaseAsync(data);

        internal void BeginInventorySell(byte[] data) => _ = HandleStorageSaleAsync(data);
    }
}
