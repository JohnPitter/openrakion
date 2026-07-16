using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        private enum EconomyLedgerKind : byte
        {
            Purchase = 0,
            Sale = 1
        }

        private sealed record EconomyLedgerEntry(
            int UserId, int CharacterId, int ItemId, int Amount,
            int PreviousBalance, int CurrentBalance, bool Cash,
            EconomyLedgerKind Kind = EconomyLedgerKind.Purchase,
            int Level = 0, long Experience = 0, string CouponLogId = "");

        private static async Task<long> WriteEconomyLedgerAsync(
            MySqlConnection connection, MySqlTransaction transaction, EconomyLedgerEntry entry)
        {
            string sql = entry.Cash
                ? "INSERT INTO logbuycashitem(userid,itemid,price,cash_prev,cash_cur," +
                  "createtime,coupon_log_id) VALUES(@u,@item,@amount,@prev,@cur,NOW(),@coupon)"
                : "INSERT INTO loguseritem(userid,characterid,itemid,gold,kind,processtime," +
                  "gold_prev,gold_cur,level,exp,coupon_log_id) " +
                  "VALUES(@u,@char,@item,@amount,@kind,NOW(),@prev,@cur,@level,@exp,@coupon)";
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@u", entry.UserId);
            command.Parameters.AddWithValue("@item", entry.ItemId);
            command.Parameters.AddWithValue("@amount", entry.Amount);
            command.Parameters.AddWithValue("@prev", entry.PreviousBalance);
            command.Parameters.AddWithValue("@cur", entry.CurrentBalance);
            command.Parameters.AddWithValue("@coupon", entry.CouponLogId);
            if (!entry.Cash)
            {
                command.Parameters.AddWithValue("@char", entry.CharacterId);
                command.Parameters.AddWithValue("@kind", (byte)entry.Kind);
                command.Parameters.AddWithValue("@level", entry.Level);
                command.Parameters.AddWithValue("@exp", entry.Experience);
            }
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new System.InvalidOperationException("Falha ao gravar ledger de economia.");
            return command.LastInsertedId;
        }
    }
}
