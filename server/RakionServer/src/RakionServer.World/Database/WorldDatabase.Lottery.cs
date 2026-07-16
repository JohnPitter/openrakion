using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database
{
    public sealed partial class WorldDatabase
    {
        public async Task<LotteryPurchaseResult> PurchaseLotteryTicketAsync(
            LotteryPurchaseCommand request)
        {
            if (request.UserId <= 0 || string.IsNullOrEmpty(request.AccountId) ||
                !LotteryRules.IsPaymentType(request.PaymentType) ||
                LotteryRules.HasRepeatedNumber(request.Numbers.ToArray()))
                return new LotteryPurchaseResult(LotteryPurchaseStatus.Rejected, 0, 0, 0);

            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                return await CommitLotteryPurchaseAsync(connection, transaction, request);
            }
            catch (Exception ex)
            {
                Log.Error("lottery", "falha ao comprar bilhete user={0}: {1}",
                    request.UserId, ex.Message);
                return new LotteryPurchaseResult(LotteryPurchaseStatus.Rejected, 0, 0, 0);
            }
        }

        private static async Task<LotteryPurchaseResult> CommitLotteryPurchaseAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            LotteryPurchaseCommand request)
        {
            bool payGold = request.PaymentType == 0;
            int gold = await LockWalletAsync(
                connection, transaction, request.UserId, request.AccountId, true);
            int cash = await LockWalletAsync(
                connection, transaction, request.UserId, request.AccountId, false);
            if (gold < 0 || cash < 0)
                throw new InvalidOperationException("Wallet da conta não encontrada.");
            int cost = LotteryRules.Cost(request.PaymentType);
            int balance = payGold ? gold : cash;
            if (balance < cost)
                return new LotteryPurchaseResult(
                    LotteryPurchaseStatus.InsufficientFunds, 0, gold, cash);

            int round = await ReadNextLotteryRoundAsync(connection, transaction);
            await InsertLotteryTicketAsync(connection, transaction, request, round);
            if (payGold) gold -= cost;
            else cash -= cost;
            await UpdateWalletAsync(connection, transaction, request.UserId,
                request.AccountId, payGold, payGold ? gold : cash);
            await transaction.CommitAsync();
            Log.Ok("lottery", "bilhete comprado user={0}, rodada={1}, pagamento={2}",
                request.UserId, round, payGold ? "gold" : "cash");
            return new LotteryPurchaseResult(LotteryPurchaseStatus.Success, round, gold, cash);
        }

        private static async Task<int> ReadNextLotteryRoundAsync(
            MySqlConnection connection, MySqlTransaction transaction)
        {
            await using var command = new MySqlCommand(
                "SELECT COALESCE(MAX(no),0)+1 FROM loglottery", connection, transaction);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static async Task InsertLotteryTicketAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            LotteryPurchaseCommand request, int round)
        {
            LotteryNumbers n = request.Numbers;
            await using var command = new MySqlCommand(
                "INSERT INTO lotto (userid,no,buytime,no1,no2,no3,no4,no5,gold,cash) " +
                "VALUES (@u,@round,NOW(),@n1,@n2,@n3,@n4,@n5,@gold,@cash)",
                connection, transaction);
            command.Parameters.AddWithValue("@u", request.UserId);
            command.Parameters.AddWithValue("@round", round);
            command.Parameters.AddWithValue("@n1", n.No1);
            command.Parameters.AddWithValue("@n2", n.No2);
            command.Parameters.AddWithValue("@n3", n.No3);
            command.Parameters.AddWithValue("@n4", n.No4);
            command.Parameters.AddWithValue("@n5", n.No5);
            command.Parameters.AddWithValue("@gold", LotteryRules.GoldCost);
            command.Parameters.AddWithValue("@cash",
                request.PaymentType == 1 ? LotteryRules.CashCost : 0);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Bilhete não foi persistido.");
        }

        public async Task<LotteryPageResult> LoadLotteryTicketsAsync(int userId, byte page)
        {
            if (userId <= 0)
                return new LotteryPageResult(LotteryPageStatus.Failed, []);
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "SELECT no,no1,no2,no3,no4,no5 FROM lotto WHERE userid=@u " +
                    "ORDER BY id DESC LIMIT @offset,@count", connection);
                command.Parameters.AddWithValue("@u", userId);
                command.Parameters.AddWithValue("@offset", page * LotteryRules.PageSize);
                command.Parameters.AddWithValue("@count", LotteryRules.PageSize);
                var tickets = new List<LotteryTicket>(LotteryRules.PageSize);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tickets.Add(new LotteryTicket(reader.GetInt32(0), new LotteryNumbers(
                        reader.GetByte(1), reader.GetByte(2), reader.GetByte(3),
                        reader.GetByte(4), reader.GetByte(5))));
                return new LotteryPageResult(
                    tickets.Count == 0 ? LotteryPageStatus.Empty : LotteryPageStatus.Success,
                    tickets);
            }
            catch (Exception ex)
            {
                Log.Error("lottery", "falha ao consultar bilhetes user={0}, página={1}: {2}",
                    userId, page, ex.Message);
                return new LotteryPageResult(LotteryPageStatus.Failed, []);
            }
        }
    }
}
