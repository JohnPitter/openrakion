using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.World.Domain;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        private sealed record PaymentResolution(
            byte FailureStatus, int Cost, int CouponRowId = 0, int CouponItemId = 0,
            int LoggedDiscount = 0);

        private sealed record PaymentQuoteRequest(
            int UserId, CharacterPayment Payment, int BaseCost, bool PayCash);

        private static async Task<PaymentResolution> ResolvePaymentAsync(
            MySqlConnection connection, MySqlTransaction transaction, PaymentQuoteRequest request)
        {
            if (!request.Payment.UsesCoupon) return new PaymentResolution(0, request.BaseCost);

            await using var itemCommand = new MySqlCommand(
                "SELECT itemid FROM useriteminfo WHERE id=@row AND userid=@u " +
                "AND characterid=0 LIMIT 1 FOR UPDATE",
                connection, transaction);
            itemCommand.Parameters.AddWithValue("@row", request.Payment.StorageRowId);
            itemCommand.Parameters.AddWithValue("@u", request.UserId);
            object? stored = await itemCommand.ExecuteScalarAsync();
            if (stored == null || Convert.ToInt32(stored) != request.Payment.ItemId ||
                request.Payment.ItemId is < 11000 or > 11999)
                return new PaymentResolution(0x14, 0);

            await using var couponCommand = new MySqlCommand(
                "SELECT discount_rate,for_cash FROM couponinfo WHERE id=@item LIMIT 1 FOR UPDATE",
                connection, transaction);
            couponCommand.Parameters.AddWithValue("@item", request.Payment.ItemId);
            await using var reader = await couponCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return new PaymentResolution(0x16, 0);
            int discountRate = reader.GetInt32(0);
            bool forCash = reader.GetInt32(1) != 0;
            await reader.DisposeAsync();
            if (forCash != request.PayCash) return new PaymentResolution(0x15, 0);
            if (discountRate is < 0 or > 100) return new PaymentResolution(0x16, 0);

            var quote = CharacterEconomyRules.ApplyCoupon(request.BaseCost, discountRate);
            return new PaymentResolution(0, quote.FinalCost, request.Payment.StorageRowId,
                request.Payment.ItemId, quote.LoggedDiscount);
        }

        private static async Task<long?> ConsumeCouponAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            PaymentResolution payment, int operationItemId)
        {
            if (payment.CouponRowId == 0) return null;

            await using (var delete = new MySqlCommand(
                "DELETE FROM useriteminfo WHERE id=@row AND userid=@u " +
                "AND characterid=0 AND itemid=@item",
                connection, transaction))
            {
                delete.Parameters.AddWithValue("@row", payment.CouponRowId);
                delete.Parameters.AddWithValue("@u", userId);
                delete.Parameters.AddWithValue("@item", payment.CouponItemId);
                if (await delete.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("Cupom mudou durante a transacao.");
            }

            await using var log = new MySqlCommand(
                "INSERT INTO logcoupon(coupon_id,item_id,user_id,use_time,discount_amount) " +
                "VALUES(@coupon,@operation,@u,NOW(),@discount)", connection, transaction);
            log.Parameters.AddWithValue("@coupon", payment.CouponItemId);
            log.Parameters.AddWithValue("@operation", operationItemId);
            log.Parameters.AddWithValue("@u", userId);
            log.Parameters.AddWithValue("@discount", payment.LoggedDiscount);
            await log.ExecuteNonQueryAsync();
            return log.LastInsertedId;
        }

        private static async Task<int[]> GrantRandomPresentAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId,
            byte characterClass, int cashCost, bool couponUsed)
        {
            if (couponUsed) return [];
            int? itemId = CharacterEconomyRules.RollPresent(cashCost, characterClass);
            if (itemId == null) return [];

            await using var pending = new MySqlCommand(
                "INSERT INTO pendingpresents(present_id,user_id,added_time) VALUES(@item,@u,NOW())",
                connection, transaction);
            pending.Parameters.AddWithValue("@item", itemId.Value);
            pending.Parameters.AddWithValue("@u", userId);
            await pending.ExecuteNonQueryAsync();
            long pendingId = pending.LastInsertedId;

            await ExecuteAsync(connection, transaction,
                "INSERT INTO logpresent(pending_id,present_id,sender_id,user_id,present_time) " +
                "VALUES(@pending,@item,0,@u,NOW())",
                ("@pending", pendingId), ("@item", itemId.Value), ("@u", userId));
            return [itemId.Value];
        }
    }
}
