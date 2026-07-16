using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public sealed class LauncherTicketRepository
{
    private readonly LauncherWebConfig _config;

    public LauncherTicketRepository(LauncherWebConfig config) => _config = config;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_config.ConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await ExecuteSchemaAsync(connection, LauncherTicketSchema.CreateSql, cancellationToken);
        foreach (string sql in LauncherTicketSchema.MigrationSql)
            await ExecuteSchemaAsync(connection, sql, cancellationToken);
    }

    public async Task<IssuedLauncherTicket?> IssueAsync(
        string account, string password, CancellationToken cancellationToken)
        => await IssueAsync(account, password, default, cancellationToken);

    public async Task<IssuedLauncherTicket?> IssueAsync(
        string account, string password, LauncherBuildIdentity build,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_config.ConnectionString!);
        await connection.OpenAsync(cancellationToken);
        string? storedPassword = await ReadPasswordAsync(connection, account, cancellationToken);
        if (storedPassword is null || !PasswordsEqual(storedPassword, password)) return null;

        string token = LauncherTicketToken.Create();
        DateTime expiresAt = DateTime.UtcNow.AddSeconds(_config.TicketLifetimeSeconds);
        await using var command = new MySqlCommand(
            "INSERT INTO launcher_ticket " +
            "(token_hash,account_id,app_id,build_version,expires_at,used_at,created_at) " +
            "VALUES (@hash,@account,@app,@build,@expires,NULL,UTC_TIMESTAMP(6))", connection);
        command.Parameters.Add("@hash", MySqlDbType.Binary, 32).Value = LauncherTicketToken.Hash(token);
        command.Parameters.AddWithValue("@account", account);
        command.Parameters.AddWithValue("@app", build.AppId);
        command.Parameters.AddWithValue("@build", build.BuildVersion);
        command.Parameters.AddWithValue("@expires", expiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new IssuedLauncherTicket(token, expiresAt);
    }

    private static async Task ExecuteSchemaAsync(
        MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> ReadPasswordAsync(
        MySqlConnection connection, string account, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            "SELECT password FROM user WHERE id=@account", connection);
        command.Parameters.AddWithValue("@account", account);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static bool PasswordsEqual(string expected, string supplied)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}

public sealed record IssuedLauncherTicket(string Ticket, DateTime ExpiresAt);
