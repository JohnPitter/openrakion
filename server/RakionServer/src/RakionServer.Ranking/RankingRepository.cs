using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.Ranking;

public sealed class RankingRepository(string sourceConnection, string projectionConnection)
{
    private const string LockName = "openrakion.rank-update";
    private readonly string _sourceConnection = sourceConnection;
    private readonly string _projectionConnection = projectionConnection;

    public async Task<MySqlConnection> AcquireSourceAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_sourceConnection);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT GET_LOCK(@name, 0)", connection);
        command.Parameters.AddWithValue("@name", LockName);
        object? acquired = await command.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(acquired) != 1)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException("Já existe uma atualização de ranking em execução.");
        }
        return connection;
    }

    public static async Task ReleaseSourceAsync(MySqlConnection connection)
    {
        try
        {
            await using var command = new MySqlCommand("SELECT RELEASE_LOCK(@name)", connection);
            command.Parameters.AddWithValue("@name", LockName);
            await command.ExecuteScalarAsync();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    public async Task<MySqlConnection> OpenProjectionAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_projectionConnection);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public static async Task<CharacterRankSource[]> LoadCharactersAsync(
        MySqlConnection source,
        int activeMonths,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.id,b.name,a.name,a.level,a.class,a.exp,a.win,a.lose,a.draw,
                   a.totalrank,a.classrank,b.country
            FROM characterinfo a
            INNER JOIN usergameinfo b ON b.id=a.userid
            WHERE b.country BETWEEN 1 AND 254
              AND a.auth NOT IN (2,10,52)
              AND b.lastconnect > DATE_SUB(NOW(), INTERVAL @months MONTH)
            ORDER BY b.country,a.exp DESC,a.id
            """;
        var rows = new List<CharacterRankSource>();
        await using var command = new MySqlCommand(sql, source);
        command.Parameters.AddWithValue("@months", activeMonths);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CharacterRankSource(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3),
                reader.GetByte(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt16(11)));
        }
        return rows.ToArray();
    }

    public static async Task<ClanMemberRankSource[]> LoadClanMembersAsync(
        MySqlConnection source,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,clanid,clanpoint
            FROM usergameinfo
            WHERE clanid > 0
            ORDER BY clanid,clanpoint DESC,id
            """;
        var rows = new List<ClanMemberRankSource>();
        await using var command = new MySqlCommand(sql, source);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new ClanMemberRankSource(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
        return rows.ToArray();
    }

    public static async Task<ClanRankSource[]> LoadClansAsync(
        MySqlConnection source,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,name,mastername,members,createtime,point,rank,country
            FROM claninfo
            ORDER BY point DESC,id
            """;
        var rows = new List<ClanRankSource>();
        await using var command = new MySqlCommand(sql, source);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ClanRankSource(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3),
                reader.GetDateTime(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt16(7)));
        }
        return rows.ToArray();
    }

    public static async Task PrepareStagingAsync(
        MySqlConnection projection,
        CancellationToken cancellationToken)
    {
        foreach ((string staging, string snapshot) in Tables)
        {
            await ExecuteAsync(projection, $"DROP TABLE IF EXISTS `{staging}`", cancellationToken);
            await ExecuteAsync(projection, $"CREATE TABLE `{staging}` LIKE `{snapshot}`", cancellationToken);
        }
    }

    public static async Task WriteStagingAsync(
        MySqlConnection projection,
        RankingProjection ranking,
        CancellationToken cancellationToken)
    {
        foreach (CharacterRank row in ranking.Characters)
        {
            await InsertTotalAsync(projection, row, cancellationToken);
            await InsertClassAsync(projection, row, cancellationToken);
        }
        foreach (ClanRank row in ranking.Clans)
            await InsertClanAsync(projection, row, cancellationToken);
    }

    public static async Task UpdateCanonicalAsync(
        MySqlConnection source,
        RankingProjection ranking,
        CancellationToken cancellationToken)
    {
        await using MySqlTransaction transaction = await source.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (CharacterRank row in ranking.Characters)
            {
                const string sql = """
                    UPDATE characterinfo
                    SET rankgrade=@grade,totalrank=@total,classrank=@class
                    WHERE id=@id
                    """;
                await ExecuteParameterizedAsync(source, transaction, sql, cancellationToken,
                    ("@grade", row.Grade), ("@total", row.TotalRank),
                    ("@class", row.ClassRank), ("@id", row.Source.Id));
            }
            foreach (ClanMemberRank row in ranking.ClanMembers)
            {
                await ExecuteParameterizedAsync(source, transaction,
                    "UPDATE usergameinfo SET clanrank=@rank WHERE id=@id", cancellationToken,
                    ("@rank", row.Rank), ("@id", row.UserGameId));
            }
            foreach (ClanRank row in ranking.Clans)
            {
                await ExecuteParameterizedAsync(source, transaction,
                    "UPDATE claninfo SET rank=@rank WHERE id=@id", cancellationToken,
                    ("@rank", row.Rank), ("@id", row.Source.Id));
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static async Task PublishAsync(MySqlConnection projection, CancellationToken cancellationToken)
    {
        foreach ((string _, string snapshot) in Tables)
            await ExecuteAsync(projection, $"DROP TABLE IF EXISTS `{snapshot}_previous`", cancellationToken);

        var renames = new List<string>(Tables.Length * 2);
        foreach ((string staging, string snapshot) in Tables)
        {
            renames.Add($"`{snapshot}` TO `{snapshot}_previous`");
            renames.Add($"`{staging}` TO `{snapshot}`");
        }
        await ExecuteAsync(projection, "RENAME TABLE " + string.Join(',', renames), cancellationToken);
    }

    public static async Task CleanupPreviousAsync(
        MySqlConnection projection,
        CancellationToken cancellationToken)
    {
        foreach ((string _, string snapshot) in Tables)
            await ExecuteAsync(projection, $"DROP TABLE IF EXISTS `{snapshot}_previous`", cancellationToken);
    }

    private static async Task InsertTotalAsync(
        MySqlConnection connection,
        CharacterRank row,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rank_total_next
              (id,rank,username,name,grade,level,exp,win,lose,draw,lastrank,classrank,class,country)
            VALUES
              (@id,@rank,@username,@name,@grade,@level,@exp,@win,@lose,@draw,@last,@classrank,@class,@country)
            """;
        CharacterRankSource source = row.Source;
        await ExecuteParameterizedAsync(connection, null, sql, cancellationToken,
            ("@id", source.Id), ("@rank", row.TotalRank), ("@username", source.Username),
            ("@name", source.Name), ("@grade", row.Grade), ("@level", source.Level),
            ("@exp", source.Experience), ("@win", source.Wins), ("@lose", source.Losses),
            ("@draw", source.Draws), ("@last", source.LastTotalRank),
            ("@classrank", source.LastClassRank), ("@class", source.Class), ("@country", source.Country));
    }

    private static async Task InsertClassAsync(
        MySqlConnection connection,
        CharacterRank row,
        CancellationToken cancellationToken)
    {
        string table = row.Source.Class switch
        {
            0 => "rank_swordman_next",
            1 => "rank_archer_next",
            2 => "rank_blacksmith_next",
            3 => "rank_mage_next",
            4 => "rank_ninja_next",
            _ => throw new InvalidOperationException($"Classe inválida no ranking: {row.Source.Class}.")
        };
        string sql = $"""
            INSERT INTO `{table}`
              (rank,username,name,grade,level,exp,win,lose,draw,lastrank,country)
            VALUES
              (@rank,@username,@name,@grade,@level,@exp,@win,@lose,@draw,@last,@country)
            """;
        CharacterRankSource source = row.Source;
        await ExecuteParameterizedAsync(connection, null, sql, cancellationToken,
            ("@rank", row.ClassRank), ("@username", source.Username), ("@name", source.Name),
            ("@grade", row.Grade), ("@level", source.Level), ("@exp", source.Experience),
            ("@win", source.Wins), ("@lose", source.Losses), ("@draw", source.Draws),
            ("@last", source.LastClassRank), ("@country", source.Country));
    }

    private static async Task InsertClanAsync(
        MySqlConnection connection,
        ClanRank row,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rank_clan_next
              (rank,clanid,name,master,members,createtime,point,lastrank,country)
            VALUES
              (@rank,@id,@name,@master,@members,@created,@points,@last,@country)
            """;
        ClanRankSource source = row.Source;
        await ExecuteParameterizedAsync(connection, null, sql, cancellationToken,
            ("@rank", row.Rank), ("@id", source.Id), ("@name", source.Name),
            ("@master", source.Master), ("@members", source.Members), ("@created", source.CreatedAt),
            ("@points", source.Points), ("@last", source.LastRank), ("@country", source.Country));
    }

    private static async Task ExecuteParameterizedAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static readonly (string Staging, string Snapshot)[] Tables =
    {
        ("rank_total_next", "totalrankp"),
        ("rank_swordman_next", "swordmanrankp"),
        ("rank_archer_next", "archerrankp"),
        ("rank_blacksmith_next", "blacksmithrankp"),
        ("rank_mage_next", "magerankp"),
        ("rank_ninja_next", "ninjarankp"),
        ("rank_clan_next", "clanrankp")
    };
}
