using RakionServer.World.Database;

namespace RakionServer.World.Domain
{
    public static class StorageEconomyRules
    {
        public static int? PurchasePrice(ItemDef? item, bool payGold)
        {
            if (item == null) return null;
            int expectedShop = payGold ? 1 : 2;
            int price = payGold ? item.Gold : item.Cash;
            return item.Shop == expectedShop && price > 0 ? price : null;
        }

        public static int SellPrice(ItemDef? item, int itemId)
        {
            if (item == null || IsPotion(itemId)) return 0;
            double price = item.Shop switch
            {
                1 => item.Gold * 0.4,
                2 => item.Cash * 1.5,
                _ => 0
            };
            return (int)System.Math.Round(price, System.MidpointRounding.AwayFromZero);
        }

        public static bool IsPotion(int itemId) => itemId is >= 12000 and <= 12999;

        public static byte PurchaseSerialType(bool payGold) => payGold ? (byte)1 : (byte)2;
    }
}
