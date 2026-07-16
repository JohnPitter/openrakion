using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Database
{
    public sealed record ClanMemberIdentity(string AccountName, string BuddyName);

    public sealed partial class WorldDatabase
    {
        public async Task<ClanLoginSnapshot> LoadClanLoginSnapshotAsync(
            int gameInfoId, int clanId)
        {
            if (clanId <= 0) return ClanLoginSnapshot.Empty;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                ClanLoginSnapshot? snapshot = await LoadClanHeaderAsync(
                    connection, gameInfoId, clanId);
                if (snapshot == null) return ClanLoginSnapshot.Empty;
                string upperCharacter = await LoadTreeUpperCharacterAsync(
                    connection, snapshot.TreeUpperAccount);
                IReadOnlyList<ClanTreeChild> children = await LoadTreeChildrenAsync(
                    connection, await LoadAccountNameAsync(connection, gameInfoId));
                return snapshot with
                {
                    TreeUpperCharacter = upperCharacter,
                    Children = children
                };
            }
            catch (Exception exception)
            {
                Log.Error("clan", "LoadClanLoginSnapshotAsync(game={0}, clan={1}): {2}",
                    gameInfoId, clanId, exception.Message);
                return ClanLoginSnapshot.Empty;
            }
        }

        public async Task<IReadOnlyList<ClanMemberIdentity>?> LoadClanMembersAsync(
            int gameInfoId, int clanId)
        {
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "SELECT LEFT(name, 16), LEFT(COALESCE(buddyname, ''), 12) " +
                    "FROM usergameinfo WHERE clanid <> 0 AND clanid = @clan " +
                    "AND id <> @self ORDER BY id LIMIT 99", connection);
                command.Parameters.AddWithValue("@clan", clanId);
                command.Parameters.AddWithValue("@self", gameInfoId);
                await using var reader = await command.ExecuteReaderAsync();
                var members = new List<ClanMemberIdentity>();
                while (await reader.ReadAsync())
                {
                    members.Add(new ClanMemberIdentity(
                        reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
                }
                return members;
            }
            catch (Exception ex)
            {
                Log.Error("clan", "LoadClanMembersAsync(game={0}, clan={1}): {2}",
                    gameInfoId, clanId, ex.Message);
                return null;
            }
        }

        private static async Task<ClanLoginSnapshot?> LoadClanHeaderAsync(
            MySqlConnection connection, int gameInfoId, int clanId)
        {
            await using var command = new MySqlCommand(
                "SELECT c.id,COALESCE(c.name,''),COALESCE(c.rank,0)," +
                "COALESCE(c.members,0),COALESCE(c.point,0),g.clanpoint,g.clanrank," +
                "COALESCE((SELECT m.charname FROM usergameinfo m " +
                "WHERE m.id=c.masterid ORDER BY m.name LIMIT 1),'')," +
                "g.treeuppername,g.treerank,COALESCE(g.clangrade,0) FROM usergameinfo g " +
                "JOIN claninfo c ON c.id=g.clanid " +
                "WHERE g.id=@game AND g.clanid=@clan LIMIT 1", connection);
            command.Parameters.AddWithValue("@game", gameInfoId);
            command.Parameters.AddWithValue("@clan", clanId);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return new ClanLoginSnapshot
            {
                Id = reader.GetInt32(0),
                Grade = checked((byte)reader.GetInt32(10)),
                Name = reader.GetString(1),
                Rank = checked((uint)reader.GetInt64(2)),
                Members = checked((ushort)reader.GetInt32(3)),
                Point = checked((uint)reader.GetInt64(4)),
                MemberPoint = checked((uint)reader.GetInt64(5)),
                MemberRank = checked((uint)reader.GetInt64(6)),
                MasterCharacterName = reader.GetString(7),
                TreeUpperAccount = reader.GetString(8),
                TreeRank = checked((byte)reader.GetInt32(9))
            };
        }

        private static async Task<string> LoadAccountNameAsync(
            MySqlConnection connection, int gameInfoId)
        {
            await using var command = new MySqlCommand(
                "SELECT name FROM usergameinfo WHERE id=@game ORDER BY name LIMIT 1", connection);
            command.Parameters.AddWithValue("@game", gameInfoId);
            return (await command.ExecuteScalarAsync()) as string ?? "";
        }

        private static async Task<string> LoadTreeUpperCharacterAsync(
            MySqlConnection connection, string upperAccount)
        {
            if (upperAccount.Length == 0) return "";
            await using var command = new MySqlCommand(
                "SELECT charname FROM usergameinfo WHERE name=@name LIMIT 1", connection);
            command.Parameters.AddWithValue("@name", upperAccount);
            return (await command.ExecuteScalarAsync()) as string ?? "";
        }

        private static async Task<IReadOnlyList<ClanTreeChild>> LoadTreeChildrenAsync(
            MySqlConnection connection, string accountName)
        {
            var children = new List<ClanTreeChild>();
            if (accountName.Length == 0) return children;
            await using var command = new MySqlCommand(
                "SELECT name,charname FROM usergameinfo " +
                "WHERE treeuppername=@owner ORDER BY id LIMIT 7", connection);
            command.Parameters.AddWithValue("@owner", accountName);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                children.Add(new ClanTreeChild(reader.GetString(0), reader.GetString(1)));
            return children;
        }
    }
}
