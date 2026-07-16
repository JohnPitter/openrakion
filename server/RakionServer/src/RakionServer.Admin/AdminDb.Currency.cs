using System.Data;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.Admin;

public sealed partial class AdminDb
{
    public async Task EnsureCurrencySchemaAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "CREATE TABLE IF NOT EXISTS admin_currency_adjustment (" +
            "id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY," +
            "account_id VARCHAR(16) NOT NULL,usergameinfo_id INT NOT NULL," +
            "currency TINYINT UNSIGNED NOT NULL,delta BIGINT NOT NULL," +
            "balance_before BIGINT NOT NULL,balance_after BIGINT NOT NULL," +
            "reason VARCHAR(200) NOT NULL,operator_id VARCHAR(64) NOT NULL," +
            "created_at DATETIME(6) NOT NULL," +
            "INDEX ix_admin_currency_account (account_id,created_at)) ENGINE=InnoDB",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<CurrencyAdjustmentResult> AdjustCurrencyAsync(
        CurrencyAdjustmentRequest request)
    {
        _identity.Demand(AdminPermission.EconomyWrite);
        request = request with { OperatorId = _identity.OperatorId };
        CurrencyAdjustmentPolicy.Validate(request);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        long previous = await LockBalanceAsync(connection, transaction, request);
        if (previous == request.TargetBalance)
        {
            await transaction.RollbackAsync();
            return new CurrencyAdjustmentResult(previous, previous, 0, false);
        }

        await UpdateBalanceAsync(connection, transaction, request);
        long delta = request.TargetBalance - previous;
        await WriteAdjustmentAsync(connection, transaction, request, previous, delta);
        await transaction.CommitAsync();
        Log.Warn("admin", "ajuste {0} conta={1}, delta={2}, saldo={3}, operador={4}",
            request.Currency, request.AccountId, delta, request.TargetBalance,
            request.OperatorId);
        return new CurrencyAdjustmentResult(
            previous, request.TargetBalance, delta, true);
    }

    public async Task<IReadOnlyList<CurrencyAdjustmentRow>> ListCurrencyAdjustmentsAsync(
        string accountId, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return [];
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT id,currency,delta,balance_before,balance_after,reason,operator_id,created_at " +
            "FROM admin_currency_adjustment WHERE account_id=@account " +
            "ORDER BY id DESC LIMIT @limit", connection);
        command.Parameters.AddWithValue("@account", accountId);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));
        var rows = new List<CurrencyAdjustmentRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new CurrencyAdjustmentRow(
                reader.GetInt64(0), (CurrencyKind)reader.GetByte(1), reader.GetInt64(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5),
                reader.GetString(6), reader.GetDateTime(7)));
        return rows;
    }

    private static async Task<long> LockBalanceAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        CurrencyAdjustmentRequest request)
    {
        if (request.Currency == CurrencyKind.Cash)
        {
            await using var seed = new MySqlCommand(
                "INSERT IGNORE INTO cash(id,cash) VALUES(@account,0)", connection, transaction);
            seed.Parameters.AddWithValue("@account", request.AccountId);
            await seed.ExecuteNonQueryAsync();
        }
        string sql = request.Currency == CurrencyKind.Gold
            ? "SELECT gold FROM usergameinfo WHERE id=@id FOR UPDATE"
            : "SELECT cash FROM cash WHERE id=@account FOR UPDATE";
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", request.GameInfoId);
        command.Parameters.AddWithValue("@account", request.AccountId);
        object? value = await command.ExecuteScalarAsync();
        return value == null
            ? throw new InvalidOperationException("Wallet não encontrada.")
            : Convert.ToInt64(value);
    }

    private static async Task UpdateBalanceAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        CurrencyAdjustmentRequest request)
    {
        string sql = request.Currency == CurrencyKind.Gold
            ? "UPDATE usergameinfo SET gold=@balance WHERE id=@id"
            : "UPDATE cash SET cash=@balance WHERE id=@account";
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@balance", request.TargetBalance);
        command.Parameters.AddWithValue("@id", request.GameInfoId);
        command.Parameters.AddWithValue("@account", request.AccountId);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Wallet mudou durante o ajuste.");
    }

    private static async Task WriteAdjustmentAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        CurrencyAdjustmentRequest request, long previous, long delta)
    {
        await using var command = new MySqlCommand(
            "INSERT INTO admin_currency_adjustment(account_id,usergameinfo_id,currency,delta," +
            "balance_before,balance_after,reason,operator_id,created_at) " +
            "VALUES(@account,@user,@currency,@delta,@before,@after,@reason,@operator,NOW(6))",
            connection, transaction);
        command.Parameters.AddWithValue("@account", request.AccountId);
        command.Parameters.AddWithValue("@user", request.GameInfoId);
        command.Parameters.AddWithValue("@currency", (byte)request.Currency);
        command.Parameters.AddWithValue("@delta", delta);
        command.Parameters.AddWithValue("@before", previous);
        command.Parameters.AddWithValue("@after", request.TargetBalance);
        command.Parameters.AddWithValue("@reason", request.Reason.Trim());
        command.Parameters.AddWithValue("@operator", request.OperatorId);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Auditoria do ajuste não foi persistida.");
    }
}
