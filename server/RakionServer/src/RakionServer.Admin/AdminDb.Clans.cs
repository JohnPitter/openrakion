using System.Data;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.Admin;

public sealed partial class AdminDb
{
    private const string ClanSnapshotSql =
        "SELECT c.id,c.masterid,c.mastername,c.name,c.point,c.members,c.rank," +
        "g.id,g.name,g.clanid,g.clanpoint,g.clanrank,g.treeuppername,g.treerank " +
        "FROM claninfo c LEFT JOIN usergameinfo g ON g.clanid=c.id " +
        "WHERE c.id=@clan ORDER BY g.id";
    private const string CreateSnapshotSql =
        "SELECT c.id,c.masterid,c.mastername,c.name,c.point,c.members,c.rank," +
        "g.id,g.name,g.clanid,g.clanpoint,g.clanrank,g.treeuppername,g.treerank " +
        "FROM usergameinfo g LEFT JOIN claninfo c ON c.id=g.clanid " +
        "WHERE g.name=@account";

    public async Task<IReadOnlyList<ClanRow>> ListClansAsync(int limit = 100)
    {
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT c.id,COALESCE(c.name,''),COALESCE(m.name,'')," +
            "COALESCE(m.charname,''),COALESCE(c.point,0),COALESCE(c.members,0)," +
            "COALESCE(c.rank,0),COALESCE(c.country,0) FROM claninfo c " +
            "LEFT JOIN usergameinfo m ON m.id=c.masterid " +
            "ORDER BY c.rank,c.id LIMIT @limit", connection);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 500));
        var rows = new List<ClanRow>();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new ClanRow(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5),
                checked((uint)reader.GetInt64(6)), reader.GetInt32(7)));
        return rows;
    }

    public async Task<IReadOnlyList<ClanMemberRow>> ListClanMembersAsync(int clanId)
    {
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT g.id,g.name,g.charname,g.clanpoint,g.clanrank,g.treeuppername," +
            "g.treerank,(g.id=c.masterid) FROM usergameinfo g " +
            "JOIN claninfo c ON c.id=g.clanid WHERE c.id=@clan " +
            "ORDER BY (g.id=c.masterid) DESC,g.clanrank,g.id", connection);
        command.Parameters.AddWithValue("@clan", clanId);
        var rows = new List<ClanMemberRow>();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new ClanMemberRow(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5),
                reader.GetInt32(6), reader.GetBoolean(7)));
        return rows;
    }

    public async Task<int> CreateClanAsync(
        string masterAccount, string clanName, string reason)
    {
        _identity.Demand(AdminPermission.ClanWrite);
        masterAccount = ClanPolicy.NormalizeAccount(masterAccount);
        clanName = ClanPolicy.NormalizeName(clanName);
        ValidateAuditFields(masterAccount, reason, $"name={clanName}");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        await EnsureClanStorageTransactionalAsync(connection, transaction);
        AccountLock master = await LockAccountAsync(connection, transaction, masterAccount);
        if (master.ClanId != 0)
            throw new InvalidOperationException("A conta já pertence a um clã.");
        var accountParameters = new[] { ("@account", (object)masterAccount) };
        string before = await SnapshotHashAsync(
            connection, transaction, CreateSnapshotSql, accountParameters);
        await EnsureClanNameAvailableAsync(connection, transaction, clanName);
        int clanId = await InsertClanAsync(connection, transaction, master, clanName);
        await AssignMemberAsync(connection, transaction, master.GameInfoId, clanId);
        await ReconcileMembersAsync(connection, transaction, clanId);
        string after = await SnapshotHashAsync(
            connection, transaction, CreateSnapshotSql, accountParameters);
        await AppendAuditAsync(connection, transaction, "clan.create", clanId.ToString(),
            reason, $"name={clanName};master={masterAccount}", before, after);
        await transaction.CommitAsync();
        Log.Warn("admin-clan", "criado clan={0} master='{1}' operador='{2}'",
            clanId, masterAccount, _identity.OperatorId);
        return clanId;
    }

    public Task AddClanMemberAsync(int clanId, string account, string reason)
    {
        _identity.Demand(AdminPermission.ClanWrite);
        account = ClanPolicy.NormalizeAccount(account);
        return MutateClanAsync(clanId, "clan.member.add", account, reason,
            async (connection, transaction) =>
            {
                await LockClanAsync(connection, transaction, clanId);
                AccountLock member = await LockAccountAsync(connection, transaction, account);
                if (member.ClanId != 0)
                    throw new InvalidOperationException("A conta já pertence a um clã.");
                if (await CountMembersAsync(connection, transaction, clanId) >= ClanPolicy.MaxMembers)
                    throw new InvalidOperationException("O clã atingiu 99 membros.");
                await AssignMemberAsync(connection, transaction, member.GameInfoId, clanId);
                await ReconcileMembersAsync(connection, transaction, clanId);
            });
    }

    public Task RemoveClanMemberAsync(int clanId, string account, string reason)
    {
        _identity.Demand(AdminPermission.ClanWrite);
        account = ClanPolicy.NormalizeAccount(account);
        return MutateClanAsync(clanId, "clan.member.remove", account, reason,
            async (connection, transaction) =>
            {
                ClanLock clan = await LockClanAsync(connection, transaction, clanId);
                AccountLock member = await LockAccountAsync(connection, transaction, account);
                EnsureMembership(member, clanId);
                if (member.GameInfoId == clan.MasterId)
                    throw new InvalidOperationException(
                        "Transfira a liderança ou dissolva o clã antes de remover o mestre.");
                await ClearMemberAsync(connection, transaction, member);
                await ReconcileMembersAsync(connection, transaction, clanId);
            });
    }

    public Task TransferClanMasterAsync(int clanId, string account, string reason)
    {
        _identity.Demand(AdminPermission.ClanWrite);
        account = ClanPolicy.NormalizeAccount(account);
        return MutateClanAsync(clanId, "clan.master.transfer", account, reason,
            async (connection, transaction) =>
            {
                await LockClanAsync(connection, transaction, clanId);
                AccountLock member = await LockAccountAsync(connection, transaction, account);
                EnsureMembership(member, clanId);
                await using var command = new MySqlCommand(
                    "UPDATE claninfo SET masterid=@id,mastername=@account WHERE id=@clan",
                    connection, transaction);
                command.Parameters.AddWithValue("@id", member.GameInfoId);
                command.Parameters.AddWithValue("@account", member.AccountName);
                command.Parameters.AddWithValue("@clan", clanId);
                if (await command.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("O clã mudou durante a transferência.");
            });
    }

    public Task SetClanTreeParentAsync(
        int clanId, string account, string parent, string reason)
    {
        _identity.Demand(AdminPermission.ClanWrite);
        account = ClanPolicy.NormalizeAccount(account);
        parent = ClanPolicy.NormalizeOptionalAccount(parent);
        return MutateClanAsync(clanId, "clan.tree.parent", account, reason,
            async (connection, transaction) =>
            {
                ClanPolicy.ValidateParent(account, parent);
                await LockClanAsync(connection, transaction, clanId);
                AccountLock member = await LockAccountAsync(connection, transaction, account);
                EnsureMembership(member, clanId);
                if (parent.Length != 0)
                {
                    AccountLock parentRow = await LockAccountAsync(
                        connection, transaction, parent);
                    EnsureMembership(parentRow, clanId);
                    await EnsureTreeCapacityAsync(
                        connection, transaction, account, parent);
                    await EnsureTreeAcyclicAsync(connection, transaction, account, parent);
                }
                await using var command = new MySqlCommand(
                    "UPDATE usergameinfo SET treeuppername=@parent WHERE id=@id",
                    connection, transaction);
                command.Parameters.AddWithValue("@parent", parent);
                command.Parameters.AddWithValue("@id", member.GameInfoId);
                await command.ExecuteNonQueryAsync();
            });
    }

    public Task DissolveClanAsync(int clanId, string reason) =>
        MutateClanAsync(clanId, "clan.dissolve", clanId.ToString(), reason,
            async (connection, transaction) =>
            {
                await LockClanAsync(connection, transaction, clanId);
                await using (var members = new MySqlCommand(
                    "UPDATE usergameinfo SET clanid=0,clanpoint=0,clanrank=0,clangrade=0," +
                    "treeuppername='',treerank=0 WHERE clanid=@clan", connection, transaction))
                {
                    members.Parameters.AddWithValue("@clan", clanId);
                    await members.ExecuteNonQueryAsync();
                }
                await using var clan = new MySqlCommand(
                    "DELETE FROM claninfo WHERE id=@clan", connection, transaction);
                clan.Parameters.AddWithValue("@clan", clanId);
                if (await clan.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("O clã mudou durante a dissolução.");
            });

    private async Task MutateClanAsync(
        int clanId, string action, string target, string reason,
        Func<MySqlConnection, MySqlTransaction, Task> mutation)
    {
        _identity.Demand(AdminPermission.ClanWrite);
        ValidateAuditFields(target, reason, $"clan={clanId}");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        await EnsureClanStorageTransactionalAsync(connection, transaction);
        var parameters = new[] { ("@clan", (object)clanId) };
        string before = await SnapshotHashAsync(
            connection, transaction, ClanSnapshotSql, parameters);
        await mutation(connection, transaction);
        string after = await SnapshotHashAsync(
            connection, transaction, ClanSnapshotSql, parameters);
        await AppendAuditAsync(connection, transaction, action, target, reason,
            $"clan={clanId}", before, after);
        await transaction.CommitAsync();
        Log.Warn("admin-clan", "action={0} clan={1} target='{2}' operador='{3}'",
            action, clanId, target, _identity.OperatorId);
    }

    private static async Task EnsureClanStorageTransactionalAsync(
        MySqlConnection connection, MySqlTransaction transaction)
    {
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() " +
            "AND table_name IN ('claninfo','usergameinfo') AND engine='InnoDB'",
            connection, transaction);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 2)
            throw new InvalidOperationException(
                "Aplique deploy/migrations/001_clan_innodb.sql antes de alterar clãs.");
    }

    private static async Task<AccountLock> LockAccountAsync(
        MySqlConnection connection, MySqlTransaction transaction, string account)
    {
        await using var command = new MySqlCommand(
            "SELECT id,name,charname,clanid,country FROM usergameinfo " +
            "WHERE name=@account LIMIT 1 FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@account", account);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Conta de jogo não encontrada.");
        return new AccountLock(
            reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4));
    }

    private static async Task<ClanLock> LockClanAsync(
        MySqlConnection connection, MySqlTransaction transaction, int clanId)
    {
        await using var command = new MySqlCommand(
            "SELECT id,masterid FROM claninfo WHERE id=@clan FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue("@clan", clanId);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Clã não encontrado.");
        return new ClanLock(reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task EnsureClanNameAvailableAsync(
        MySqlConnection connection, MySqlTransaction transaction, string name)
    {
        await using var command = new MySqlCommand(
            "SELECT id FROM claninfo WHERE name=@name FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@name", name);
        if (await command.ExecuteScalarAsync() != null)
            throw new InvalidOperationException("Já existe um clã com esse nome.");
    }

    private static async Task<int> InsertClanAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        AccountLock master, string name)
    {
        await using var command = new MySqlCommand(
            "INSERT INTO claninfo(masterid,mastername,name,point,members,rank,createtime,country) " +
            "VALUES(@master,@account,@name,0,1,0,NOW(),@country)",
            connection, transaction);
        command.Parameters.AddWithValue("@master", master.GameInfoId);
        command.Parameters.AddWithValue("@account", master.AccountName);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@country", master.Country);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("O clã não foi criado.");
        return checked((int)command.LastInsertedId);
    }

    private static async Task AssignMemberAsync(
        MySqlConnection connection, MySqlTransaction transaction, int gameInfoId, int clanId)
    {
        await using var command = new MySqlCommand(
            "UPDATE usergameinfo SET clanid=@clan,clanpoint=0,clanrank=0,clangrade=0," +
            "treeuppername='',treerank=0 WHERE id=@id AND clanid=0", connection, transaction);
        command.Parameters.AddWithValue("@clan", clanId);
        command.Parameters.AddWithValue("@id", gameInfoId);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("A associação mudou durante a operação.");
    }

    private static async Task ClearMemberAsync(
        MySqlConnection connection, MySqlTransaction transaction, AccountLock member)
    {
        await using (var children = new MySqlCommand(
            "UPDATE usergameinfo SET treeuppername='' WHERE treeuppername=@account",
            connection, transaction))
        {
            children.Parameters.AddWithValue("@account", member.AccountName);
            await children.ExecuteNonQueryAsync();
        }
        await using var command = new MySqlCommand(
            "UPDATE usergameinfo SET clanid=0,clanpoint=0,clanrank=0,clangrade=0," +
            "treeuppername='',treerank=0 WHERE id=@id", connection, transaction);
        command.Parameters.AddWithValue("@id", member.GameInfoId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountMembersAsync(
        MySqlConnection connection, MySqlTransaction transaction, int clanId)
    {
        await using var command = new MySqlCommand(
            "SELECT id FROM usergameinfo WHERE clanid=@clan FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue("@clan", clanId);
        int count = 0;
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) count++;
        return count;
    }

    private static async Task ReconcileMembersAsync(
        MySqlConnection connection, MySqlTransaction transaction, int clanId)
    {
        await using var command = new MySqlCommand(
            "UPDATE claninfo SET members=(SELECT COUNT(*) FROM usergameinfo " +
            "WHERE clanid=@clan) WHERE id=@clan", connection, transaction);
        command.Parameters.AddWithValue("@clan", clanId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTreeCapacityAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        string account, string parent)
    {
        await using var command = new MySqlCommand(
            "SELECT name FROM usergameinfo WHERE treeuppername=@parent " +
            "AND name<>@account FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue("@parent", parent);
        command.Parameters.AddWithValue("@account", account);
        int count = 0;
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) count++;
        if (count >= ClanPolicy.MaxTreeChildren)
            throw new InvalidOperationException("O superior já possui sete filhos na árvore.");
    }

    private static async Task EnsureTreeAcyclicAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        string account, string parent)
    {
        string current = parent;
        for (int depth = 0; depth < ClanPolicy.MaxMembers && current.Length != 0; depth++)
        {
            if (string.Equals(current, account, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A alteração criaria um ciclo na árvore.");
            await using var command = new MySqlCommand(
                "SELECT treeuppername FROM usergameinfo WHERE name=@name FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@name", current);
            current = (await command.ExecuteScalarAsync()) as string ?? "";
        }
        if (current.Length != 0)
            throw new InvalidOperationException("A árvore excede o limite seguro de profundidade.");
    }

    private static void EnsureMembership(AccountLock account, int clanId)
    {
        if (account.ClanId != clanId)
            throw new InvalidOperationException("A conta não pertence a esse clã.");
    }

    private sealed record AccountLock(
        int GameInfoId, string AccountName, string CharacterName, int ClanId, int Country);

    private sealed record ClanLock(int Id, int MasterId);
}
