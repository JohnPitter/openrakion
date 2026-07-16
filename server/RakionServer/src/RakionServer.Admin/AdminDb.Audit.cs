using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace RakionServer.Admin;

public sealed partial class AdminDb
{
    public async Task EnsureAuditSchemaAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "CREATE TABLE IF NOT EXISTS admin_audit (" +
            "id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY," +
            "operator_id VARCHAR(64) NOT NULL,role VARCHAR(16) NOT NULL," +
            "action VARCHAR(64) NOT NULL,target VARCHAR(128) NOT NULL," +
            "reason VARCHAR(200) NOT NULL,details VARCHAR(1000) NOT NULL," +
            "before_hash CHAR(64) NOT NULL,after_hash CHAR(64) NOT NULL," +
            "previous_hash CHAR(64) NOT NULL,entry_hash CHAR(64) NOT NULL," +
            "created_at DATETIME(6) NOT NULL," +
            "UNIQUE KEY ux_admin_audit_hash(entry_hash)," +
            "INDEX ix_admin_audit_target(target,created_at)) ENGINE=InnoDB",
            connection);
        await command.ExecuteNonQueryAsync();
        await using var head = new MySqlCommand(
            "CREATE TABLE IF NOT EXISTS admin_audit_head (" +
            "id TINYINT NOT NULL PRIMARY KEY,current_hash CHAR(64) NOT NULL) ENGINE=InnoDB;" +
            "INSERT IGNORE INTO admin_audit_head(id,current_hash) VALUES(1,@zero)", connection);
        head.Parameters.AddWithValue("@zero", new string('0', 64));
        await head.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAuditedAsync(
        AdminPermission permission, string action, string target, string reason,
        string details, string snapshotSql, string mutationSql,
        params (string Name, object Value)[] parameters)
    {
        _identity.Demand(permission);
        ValidateAuditFields(target, reason, details);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        string beforeHash = await SnapshotHashAsync(
            connection, transaction, snapshotSql, parameters);
        await ExecuteAsync(connection, transaction, mutationSql, parameters);
        string afterHash = await SnapshotHashAsync(
            connection, transaction, snapshotSql, parameters);
        await AppendAuditAsync(connection, transaction, action, target, reason,
            details, beforeHash, afterHash);
        await transaction.CommitAsync();
    }

    private async Task ExecuteAuditedBatchAsync(
        AdminPermission permission, string action, string target, string reason,
        string details, string snapshotSql,
        Func<MySqlConnection, MySqlTransaction, Task> mutation)
    {
        _identity.Demand(permission);
        ValidateAuditFields(target, reason, details);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        string beforeHash = await SnapshotHashAsync(connection, transaction, snapshotSql, []);
        await mutation(connection, transaction);
        string afterHash = await SnapshotHashAsync(connection, transaction, snapshotSql, []);
        await AppendAuditAsync(connection, transaction, action, target, reason,
            details, beforeHash, afterHash);
        await transaction.CommitAsync();
    }

    private static void ValidateAuditFields(string target, string reason, string details)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200)
            throw new ArgumentException("Motivo é obrigatório e deve ter até 200 caracteres.");
        if (target.Length > 128 || details.Length > 1000)
            throw new ArgumentException("Metadados de auditoria excedem o limite.");
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection, MySqlTransaction transaction, string sql,
        IReadOnlyList<(string Name, object Value)> parameters)
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> SnapshotHashAsync(
        MySqlConnection connection, MySqlTransaction transaction, string sql,
        IReadOnlyList<(string Name, object Value)> parameters)
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        AddParameters(command, parameters);
        var canonical = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string value = reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i),
                    System.Globalization.CultureInfo.InvariantCulture) ?? "";
                canonical.Append(value.Length).Append(':').Append(value).Append('|');
            }
            canonical.Append('\n');
        }
        return Hash(canonical.ToString());
    }

    private async Task AppendAuditAsync(
        MySqlConnection connection, MySqlTransaction transaction, string action,
        string target, string reason, string details, string beforeHash, string afterHash)
    {
        string previousHash = await LastAuditHashAsync(connection, transaction);
        DateTime createdAt = DateTime.UtcNow;
        string entryHash = Hash(string.Join("\n", previousHash, _identity.OperatorId,
            _identity.Role, action, target, reason.Trim(), details, beforeHash, afterHash,
            createdAt.ToString("O")));
        await using var command = new MySqlCommand(
            "INSERT INTO admin_audit(operator_id,role,action,target,reason,details,before_hash," +
            "after_hash,previous_hash,entry_hash,created_at) VALUES(@operator,@role,@action,@target," +
            "@reason,@details,@before,@after,@previous,@entry,@created)", connection, transaction);
        command.Parameters.AddWithValue("@operator", _identity.OperatorId);
        command.Parameters.AddWithValue("@role", _identity.Role.ToString());
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@target", target);
        command.Parameters.AddWithValue("@reason", reason.Trim());
        command.Parameters.AddWithValue("@details", details);
        command.Parameters.AddWithValue("@before", beforeHash);
        command.Parameters.AddWithValue("@after", afterHash);
        command.Parameters.AddWithValue("@previous", previousHash);
        command.Parameters.AddWithValue("@entry", entryHash);
        command.Parameters.AddWithValue("@created", createdAt);
        await command.ExecuteNonQueryAsync();
        await using var updateHead = new MySqlCommand(
            "UPDATE admin_audit_head SET current_hash=@entry WHERE id=1",
            connection, transaction);
        updateHead.Parameters.AddWithValue("@entry", entryHash);
        await updateHead.ExecuteNonQueryAsync();
    }

    private static async Task<string> LastAuditHashAsync(
        MySqlConnection connection, MySqlTransaction transaction)
    {
        await using var command = new MySqlCommand(
            "SELECT current_hash FROM admin_audit_head WHERE id=1 FOR UPDATE",
            connection, transaction);
        return Convert.ToString(await command.ExecuteScalarAsync())
            ?? throw new InvalidOperationException("Cabeça da auditoria não foi inicializada.");
    }

    private static void AddParameters(
        MySqlCommand command, IReadOnlyList<(string Name, object Value)> parameters)
    {
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
