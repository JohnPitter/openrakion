using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.LauncherWeb;

public sealed class LauncherTicketRepository
{
    private readonly LauncherWebConfig _config;
    private readonly ActiveAccountLookup _activeAccounts;

    public LauncherTicketRepository(
        LauncherWebConfig config, ActiveAccountLookup activeAccounts)
    {
        _config = config;
        _activeAccounts = activeAccounts;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_config.ConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await ExecuteSchemaAsync(connection, LauncherTicketSchema.CreateSql, cancellationToken);
        foreach (string sql in LauncherTicketSchema.MigrationSql)
            await ExecuteSchemaAsync(connection, sql, cancellationToken);
    }

    public async Task<LauncherTicketIssueResult> IssueAsync(
        string account, string password, CancellationToken cancellationToken)
        => await IssueAsync(account, password, default, cancellationToken);

    public async Task<LauncherTicketIssueResult> IssueAsync(
        string account, string password, LauncherBuildIdentity build,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_config.ConnectionString!);
        await connection.OpenAsync(cancellationToken);
        string? storedPassword = await ReadPasswordAsync(connection, account, cancellationToken);
        if (storedPassword is null || !PasswordsEqual(storedPassword, password))
            return new(LauncherTicketIssueStatus.InvalidCredentials, null);
        if (_activeAccounts.Contains(account))
            return new(LauncherTicketIssueStatus.AccountInUse, null);

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
        IReadOnlyList<OnlineLauncherFriend> friends = await LoadOnlineFriendsAsync(
            connection, account, cancellationToken);
        return new(LauncherTicketIssueStatus.Success,
            new IssuedLauncherTicket(token, expiresAt, friends));
    }

    public async Task<IReadOnlyList<OnlineLauncherFriend>?> AuthenticateFriendsAsync(
        string account, string password, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_config.ConnectionString!);
        await connection.OpenAsync(cancellationToken);
        string? storedPassword = await ReadPasswordAsync(connection, account, cancellationToken);
        return storedPassword is not null && PasswordsEqual(storedPassword, password)
            ? await LoadOnlineFriendsAsync(connection, account, cancellationToken)
            : null;
    }

    private async Task<IReadOnlyList<OnlineLauncherFriend>> LoadOnlineFriendsAsync(
        MySqlConnection connection, string account, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            "SELECT r.buddy_account," +
            "COALESCE(NULLIF(g.buddyname,''),NULLIF(g.charname,''),r.buddy_account) " +
            "FROM buddy_relation r LEFT JOIN usergameinfo g ON g.name=r.buddy_account " +
            "WHERE r.owner_account=@account ORDER BY r.created_at LIMIT 500", connection);
        command.Parameters.AddWithValue("@account", account);
        var friends = new List<OnlineLauncherFriend>();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            friends.Add(new OnlineLauncherFriend(reader.GetString(0), reader.GetString(1)));
        string[] online = _activeAccounts.FilterOnline(
            friends.Select(friend => friend.AccountId));
        var onlineSet = online.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return friends.Where(friend => onlineSet.Contains(friend.AccountId)).ToArray();
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

public sealed record OnlineLauncherFriend(string AccountId, string DisplayName);
public sealed record IssuedLauncherTicket(
    string Ticket, DateTime ExpiresAt, IReadOnlyList<OnlineLauncherFriend> OnlineFriends);
public sealed record LauncherTicketIssueResult(
    LauncherTicketIssueStatus Status, IssuedLauncherTicket? Ticket);
public enum LauncherTicketIssueStatus { Success, InvalidCredentials, AccountInUse }
