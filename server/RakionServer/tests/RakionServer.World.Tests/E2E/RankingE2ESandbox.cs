using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.World.Tests.E2E
{
    internal sealed class RankingE2ESandbox : IAsyncDisposable
    {
        private static readonly string[] Snapshots =
        {
            "totalrankp", "swordmanrankp", "archerrankp", "blacksmithrankp",
            "magerankp", "ninjarankp", "clanrankp"
        };

        private readonly string _connectionString;
        private readonly string _suffix = Guid.NewGuid().ToString("N");
        private bool _backedUp;

        private RankingE2ESandbox(string connectionString) =>
            _connectionString = connectionString;

        public static async Task<RankingE2ESandbox> CreateAsync(string connectionString)
        {
            var sandbox = new RankingE2ESandbox(connectionString);
            await sandbox.BackupAsync();
            try
            {
                await sandbox.PrepareAsync();
                return sandbox;
            }
            catch
            {
                await sandbox.DisposeAsync();
                throw;
            }
        }

        public async Task<RankingDatabaseState> ReadStateAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            RankingCharacterState canonical = await ReadCharacterAsync(connection);
            RankingSnapshotState total = await ReadSnapshotAsync(connection, "totalrankp");
            RankingSnapshotState byClass = await ReadSnapshotAsync(connection, "archerrankp");
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string table in Snapshots)
                counts[table] = await ScalarAsync<int>(connection,
                    $"SELECT COUNT(*) FROM `{table}`");
            int transientTables = await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM information_schema.tables " +
                "WHERE table_schema=DATABASE() AND (table_name IN " +
                "('rank_total_next','rank_swordman_next','rank_archer_next'," +
                "'rank_blacksmith_next','rank_mage_next','rank_ninja_next','rank_clan_next') " +
                "OR table_name LIKE '%rankp_previous')");
            return new RankingDatabaseState(canonical, total, byClass, counts, transientTables);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_backedUp) return;
            await using var connection = await OpenAsync(_connectionString);
            await RestoreCanonicalAsync(connection);
            await RestoreSnapshotsAsync(connection);
            _backedUp = false;
        }

        private async Task BackupAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await ExecuteAsync(connection,
                $"CREATE TABLE `{CharacterBackup}` AS " +
                "SELECT id,exp,auth,class,rankgrade,totalrank,classrank FROM characterinfo");
            await ExecuteAsync(connection,
                $"CREATE TABLE `{UserBackup}` AS " +
                "SELECT id,lastconnect,country,clanrank FROM usergameinfo");
            await ExecuteAsync(connection,
                $"CREATE TABLE `{ClanBackup}` AS SELECT id,rank FROM claninfo");
            foreach (string snapshot in Snapshots)
            {
                string backup = SnapshotBackup(snapshot);
                await ExecuteAsync(connection, $"CREATE TABLE `{backup}` LIKE `{snapshot}`");
                await ExecuteAsync(connection,
                    $"INSERT INTO `{backup}` SELECT * FROM `{snapshot}`");
            }
            _backedUp = true;
        }

        private async Task PrepareAsync()
        {
            await using var connection = await OpenAsync(_connectionString);
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction,
                "UPDATE usergameinfo SET country=2,lastconnect=NOW() WHERE id=9001");
            await ExecuteAsync(connection, transaction,
                "UPDATE characterinfo SET exp=5000,auth=0,class=1," +
                "rankgrade=99,totalrank=77,classrank=88 WHERE id=9001");
            await transaction.CommitAsync();
        }

        private async Task RestoreCanonicalAsync(MySqlConnection connection)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction,
                $"UPDATE characterinfo c JOIN `{CharacterBackup}` b ON b.id=c.id " +
                "SET c.exp=b.exp,c.auth=b.auth,c.class=b.class,c.rankgrade=b.rankgrade," +
                "c.totalrank=b.totalrank,c.classrank=b.classrank");
            await ExecuteAsync(connection, transaction,
                $"UPDATE usergameinfo u JOIN `{UserBackup}` b ON b.id=u.id " +
                "SET u.lastconnect=b.lastconnect,u.country=b.country,u.clanrank=b.clanrank");
            await ExecuteAsync(connection, transaction,
                $"UPDATE claninfo c JOIN `{ClanBackup}` b ON b.id=c.id SET c.rank=b.rank");
            await transaction.CommitAsync();
            await ExecuteAsync(connection,
                $"DROP TABLE `{CharacterBackup}`,`{UserBackup}`,`{ClanBackup}`");
        }

        private async Task RestoreSnapshotsAsync(MySqlConnection connection)
        {
            var renames = new List<string>(Snapshots.Length * 2);
            foreach (string snapshot in Snapshots)
            {
                renames.Add($"`{snapshot}` TO `{SnapshotTrash(snapshot)}`");
                renames.Add($"`{SnapshotBackup(snapshot)}` TO `{snapshot}`");
            }
            await ExecuteAsync(connection, "RENAME TABLE " + string.Join(',', renames));
            foreach (string snapshot in Snapshots)
                await ExecuteAsync(connection, $"DROP TABLE `{SnapshotTrash(snapshot)}`");
        }

        private static async Task<RankingCharacterState> ReadCharacterAsync(
            MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                "SELECT rankgrade,totalrank,classrank FROM characterinfo WHERE id=9001", connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Personagem ranking E2E ausente.");
            return new RankingCharacterState(
                reader.GetByte(0), reader.GetInt32(1), reader.GetInt32(2));
        }

        private static async Task<RankingSnapshotState> ReadSnapshotAsync(
            MySqlConnection connection, string table)
        {
            await using var command = new MySqlCommand(
                $"SELECT rank,grade,lastrank FROM `{table}` " +
                "WHERE username='test2' AND name='ProbeTwo'", connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException($"Snapshot {table} não publicou test2.");
            return new RankingSnapshotState(
                reader.GetInt32(0), reader.GetByte(1), reader.GetInt32(2));
        }

        private string CharacterBackup => $"e2e_rank_{_suffix}_character";
        private string UserBackup => $"e2e_rank_{_suffix}_user";
        private string ClanBackup => $"e2e_rank_{_suffix}_clan";
        private string SnapshotBackup(string table) => $"e2e_rank_{_suffix}_{table}";
        private string SnapshotTrash(string table) => $"e2e_trash_{_suffix}_{table}";

        private static async Task<MySqlConnection> OpenAsync(string connectionString)
        {
            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task<T> ScalarAsync<T>(MySqlConnection connection, string sql)
        {
            await using var command = new MySqlCommand(sql, connection);
            object? value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        private static async Task ExecuteAsync(MySqlConnection connection, string sql)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task ExecuteAsync(
            MySqlConnection connection, MySqlTransaction transaction, string sql)
        {
            await using var command = new MySqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
    }

    internal readonly record struct RankingCharacterState(
        byte Grade, int TotalRank, int ClassRank);
    internal readonly record struct RankingSnapshotState(int Rank, byte Grade, int LastRank);
    internal sealed record RankingDatabaseState(
        RankingCharacterState Canonical, RankingSnapshotState Total,
        RankingSnapshotState ByClass, IReadOnlyDictionary<string, int> SnapshotCounts,
        int TransientTableCount);
}
