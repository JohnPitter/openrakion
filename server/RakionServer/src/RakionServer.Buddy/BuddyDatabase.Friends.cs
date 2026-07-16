using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;

namespace RakionServer.Buddy;

public sealed partial class BuddyDatabase
{
    public async Task<IReadOnlyList<BuddyFriendRecord>> LoadFriendsAsync(string ownerAccount)
    {
        await using MySqlConnection connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT r.buddy_account," +
            "COALESCE(NULLIF(g.buddyname,''),NULLIF(g.charname,''),r.buddy_account)," +
            "r.group_name,r.ext_data FROM buddy_relation r " +
            "LEFT JOIN usergameinfo g ON g.name=r.buddy_account " +
            "WHERE r.owner_account=@owner ORDER BY r.created_at,r.buddy_account LIMIT 500",
            connection);
        command.Parameters.AddWithValue("@owner", ownerAccount);
        var friends = new List<BuddyFriendRecord>();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            friends.Add(new BuddyFriendRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), (byte[])reader[3]));
        return friends;
    }

    public async Task<IReadOnlyList<BuddyGroupRecord>> LoadGroupsAsync(string ownerAccount)
    {
        await using MySqlConnection connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT group_id,name,flags FROM buddy_group " +
            "WHERE owner_account=@owner ORDER BY sort_order,group_id LIMIT 50", connection);
        command.Parameters.AddWithValue("@owner", ownerAccount);
        var groups = new List<BuddyGroupRecord>();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            groups.Add(new BuddyGroupRecord(
                reader.GetUInt16(0), reader.GetString(1), reader.GetUInt16(2)));
        return groups;
    }

    public async Task<BuddyFriendRecord?> AddFriendAsync(
        string ownerAccount, string buddyAccount, byte[] extension)
    {
        if (string.Equals(ownerAccount, buddyAccount, StringComparison.OrdinalIgnoreCase)) return null;
        await using MySqlConnection connection = await OpenAsync();
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        if (!await AccountsExistAsync(connection, transaction, ownerAccount, buddyAccount)) return null;
        await UpsertRelationAsync(connection, transaction,
            ownerAccount, buddyAccount, extension);
        await UpsertRelationAsync(connection, transaction,
            buddyAccount, ownerAccount, new byte[BuddyFriendCodec.ExtensionLength]);
        BuddyFriendRecord? friend = await LoadFriendAsync(
            connection, transaction, ownerAccount, buddyAccount);
        await transaction.CommitAsync();
        return friend;
    }

    public async Task<bool> RemoveFriendAsync(string ownerAccount, string buddyAccount)
    {
        await using MySqlConnection connection = await OpenAsync();
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync();
        await using var command = new MySqlCommand(
            "DELETE FROM buddy_relation WHERE " +
            "(owner_account=@owner AND buddy_account=@buddy) OR " +
            "(owner_account=@buddy AND buddy_account=@owner)", connection, transaction);
        command.Parameters.AddWithValue("@owner", ownerAccount);
        command.Parameters.AddWithValue("@buddy", buddyAccount);
        int changed = await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return changed > 0;
    }

    public async Task<bool> AddGroupAsync(string ownerAccount, BuddyGroupRecord group)
    {
        try
        {
            await using MySqlConnection connection = await OpenAsync();
            await using var command = new MySqlCommand(
                "INSERT INTO buddy_group(owner_account,group_id,name,flags,sort_order) " +
                "VALUES(@owner,@id,@name,@flags,@id)", connection);
            command.Parameters.AddWithValue("@owner", ownerAccount);
            command.Parameters.AddWithValue("@id", group.Id);
            command.Parameters.AddWithValue("@name", group.Name);
            command.Parameters.AddWithValue("@flags", group.Flags);
            return await command.ExecuteNonQueryAsync() == 1;
        }
        catch (MySqlException exception) when (exception.Number == 1062) { return false; }
    }

    public async Task<bool> RenameGroupAsync(
        string ownerAccount, string oldName, string newName)
    {
        try
        {
            await using MySqlConnection connection = await OpenAsync();
            await using MySqlTransaction transaction = await connection.BeginTransactionAsync();
            await using (var group = new MySqlCommand(
                "UPDATE buddy_group SET name=@new WHERE owner_account=@owner AND name=@old",
                connection, transaction))
            {
                AddGroupNameParameters(group, ownerAccount, oldName, newName);
                if (await group.ExecuteNonQueryAsync() != 1) return false;
            }
            await using (var relations = new MySqlCommand(
                "UPDATE buddy_relation SET group_name=@new " +
                "WHERE owner_account=@owner AND group_name=@old", connection, transaction))
            {
                AddGroupNameParameters(relations, ownerAccount, oldName, newName);
                await relations.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            return true;
        }
        catch (MySqlException exception) when (exception.Number == 1062) { return false; }
    }

    public async Task<bool> AssignGroupAsync(
        string ownerAccount, string groupName, IReadOnlyList<string> buddyAccounts)
    {
        await using MySqlConnection connection = await OpenAsync();
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync();
        if (!await GroupExistsAsync(connection, transaction, ownerAccount, groupName)) return false;
        foreach (string buddy in buddyAccounts)
        {
            await using var command = new MySqlCommand(
                "UPDATE buddy_relation SET group_name=@group " +
                "WHERE owner_account=@owner AND buddy_account=@buddy", connection, transaction);
            command.Parameters.AddWithValue("@group", groupName);
            command.Parameters.AddWithValue("@owner", ownerAccount);
            command.Parameters.AddWithValue("@buddy", buddy);
            if (await command.ExecuteNonQueryAsync() != 1) return false;
        }
        await transaction.CommitAsync();
        return true;
    }

    public async Task<bool> SetFriendExtensionAsync(
        string ownerAccount, string buddyAccount, byte[] extension)
    {
        await using MySqlConnection connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "UPDATE buddy_relation SET ext_data=@ext " +
            "WHERE owner_account=@owner AND buddy_account=@buddy", connection);
        command.Parameters.AddWithValue("@ext", extension);
        command.Parameters.AddWithValue("@owner", ownerAccount);
        command.Parameters.AddWithValue("@buddy", buddyAccount);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task SaveProfileAsync(string accountId, string? guildName, byte[]? extension)
    {
        await using MySqlConnection connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "INSERT INTO buddy_profile(account_id,guild_name,ext_user,updated_at) " +
            "VALUES(@account,@guild,@ext,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE " +
            "guild_name=IF(@hasGuild=0,guild_name,@guild)," +
            "ext_user=IF(@hasExt=0,ext_user,@ext),updated_at=UTC_TIMESTAMP(6)", connection);
        command.Parameters.AddWithValue("@account", accountId);
        command.Parameters.AddWithValue("@guild", guildName ?? "");
        command.Parameters.AddWithValue("@ext", extension ?? new byte[16]);
        command.Parameters.AddWithValue("@hasGuild", guildName == null ? 0 : 1);
        command.Parameters.AddWithValue("@hasExt", extension == null ? 0 : 1);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> AccountsExistAsync(
        MySqlConnection connection, MySqlTransaction transaction, string first, string second)
    {
        await using var command = new MySqlCommand(
            "SELECT id FROM user WHERE id IN (@first,@second) FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue("@first", first);
        command.Parameters.AddWithValue("@second", second);
        int count = 0;
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) count++;
        return count == 2;
    }

    private static async Task UpsertRelationAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        string owner, string buddy, byte[] extension)
    {
        await using var command = new MySqlCommand(
            "INSERT INTO buddy_relation(owner_account,buddy_account,group_name,ext_data,created_at) " +
            "VALUES(@owner,@buddy,'',@ext,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE " +
            "ext_data=VALUES(ext_data)", connection, transaction);
        command.Parameters.AddWithValue("@owner", owner);
        command.Parameters.AddWithValue("@buddy", buddy);
        command.Parameters.AddWithValue("@ext", extension);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<BuddyFriendRecord?> LoadFriendAsync(
        MySqlConnection connection, MySqlTransaction transaction, string owner, string buddy)
    {
        await using var command = new MySqlCommand(
            "SELECT r.buddy_account," +
            "COALESCE(NULLIF(g.buddyname,''),NULLIF(g.charname,''),r.buddy_account)," +
            "r.group_name,r.ext_data FROM buddy_relation r " +
            "LEFT JOIN usergameinfo g ON g.name=r.buddy_account " +
            "WHERE r.owner_account=@owner AND r.buddy_account=@buddy", connection, transaction);
        command.Parameters.AddWithValue("@owner", owner);
        command.Parameters.AddWithValue("@buddy", buddy);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new BuddyFriendRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), (byte[])reader[3])
            : null;
    }

    private static async Task<bool> GroupExistsAsync(
        MySqlConnection connection, MySqlTransaction transaction, string owner, string groupName)
    {
        await using var command = new MySqlCommand(
            "SELECT 1 FROM buddy_group WHERE owner_account=@owner AND name=@name FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue("@owner", owner);
        command.Parameters.AddWithValue("@name", groupName);
        return await command.ExecuteScalarAsync() != null;
    }

    private static void AddGroupNameParameters(
        MySqlCommand command, string owner, string oldName, string newName)
    {
        command.Parameters.AddWithValue("@owner", owner);
        command.Parameters.AddWithValue("@old", oldName);
        command.Parameters.AddWithValue("@new", newName);
    }

    private static async Task EnsureFriendSchemaAsync(MySqlConnection connection)
    {
        await ExecuteAsync(connection,
            "CREATE TABLE IF NOT EXISTS buddy_group(" +
            "owner_account VARCHAR(16) NOT NULL,group_id SMALLINT UNSIGNED NOT NULL," +
            "name VARCHAR(20) NOT NULL,flags SMALLINT UNSIGNED NOT NULL,sort_order INT NOT NULL," +
            "PRIMARY KEY(owner_account,group_id),UNIQUE KEY uq_buddy_group_name(owner_account,name)) " +
            "ENGINE=InnoDB");
        await ExecuteAsync(connection,
            "CREATE TABLE IF NOT EXISTS buddy_relation(" +
            "owner_account VARCHAR(16) NOT NULL,buddy_account VARCHAR(16) NOT NULL," +
            "group_name VARCHAR(20) NOT NULL,ext_data BINARY(32) NOT NULL," +
            "created_at DATETIME(6) NOT NULL,PRIMARY KEY(owner_account,buddy_account)," +
            "INDEX ix_buddy_relation_reverse(buddy_account,owner_account)) ENGINE=InnoDB");
        await ExecuteAsync(connection,
            "CREATE TABLE IF NOT EXISTS buddy_profile(" +
            "account_id VARCHAR(16) NOT NULL PRIMARY KEY,guild_name VARCHAR(20) NOT NULL," +
            "ext_user BINARY(16) NOT NULL,updated_at DATETIME(6) NOT NULL) ENGINE=InnoDB");
        if (await TableExistsAsync(connection, "buddylist"))
            await ExecuteAsync(connection,
                "INSERT IGNORE INTO buddy_relation(" +
                "owner_account,buddy_account,group_name,ext_data,created_at) " +
                "SELECT TRIM(Id),TRIM(Buddy),TRIM(Category),REPEAT(CHAR(0),32),UTC_TIMESTAMP(6) " +
                "FROM buddylist WHERE TRIM(Id)<>'' AND TRIM(Buddy)<>''");
    }

    private static async Task<bool> TableExistsAsync(MySqlConnection connection, string table)
    {
        await using var command = new MySqlCommand(
            "SELECT 1 FROM information_schema.tables " +
            "WHERE table_schema=DATABASE() AND table_name=@table", connection);
        command.Parameters.AddWithValue("@table", table);
        return await command.ExecuteScalarAsync() != null;
    }
}
