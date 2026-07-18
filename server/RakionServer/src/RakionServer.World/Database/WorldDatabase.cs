using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    /// <summary>
    /// Acesso ao MySQL/MariaDB do world (db `rakion`). Toda regra de login/persistencia
    /// fica aqui (backend). Tabelas reconstruidas do dump v258: user, usergameinfo,
    /// loguserconnect, usercount.
    /// </summary>
    public sealed partial class WorldDatabase
    {
        private readonly string _conn;

        public WorldDatabase(WorldConfig.DbConfig cfg) => _conn = cfg.ConnectionString;

        /// <summary>Testa a conexao e conta tabelas; loga o resultado.</summary>
        public async Task<bool> PingAsync()
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()", c);
                long tables = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Log.Ok("db", "conectado — {0} tabelas no schema", tables);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("db", "falha ao conectar: {0}", ex.Message);
                return false;
            }
        }

        public sealed class Account
        {
            public string Id = "";
            public int Authority;
            public int Country;
            public bool Banned;
        }

        /// <summary>
        /// Autentica (id + senha em texto) contra a tabela `user`. Retorna a conta ou null.
        /// Espelha o login direto do world quando [Authentication] Type=0 (sem auth.asp).
        /// </summary>
        public async Task<Account?> AuthenticateAsync(string id, string password)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT password, Authority, country FROM user WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                {
                    Log.Warn("db", "login: id '{0}' nao existe", id);
                    return null;
                }
                string dbPass = r.GetString(0);
                if (!string.Equals(dbPass, password, StringComparison.Ordinal))
                {
                    Log.Warn("db", "login: senha incorreta para '{0}'", id);
                    return null;
                }
                return new Account
                {
                    Id = id,
                    Authority = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Country = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                };
            }
            catch (Exception ex)
            {
                Log.Error("db", "AuthenticateAsync('{0}'): {1}", id, ex.Message);
                return null;
            }
        }

        public sealed class GameInfo
        {
            public int Id;
            public string Name = "";
            public string CharName = "";
            public int Gold;
            public bool Ban;
            public string BanReason = "";
            public int PowerLevelPoint;   // usergameinfo.powerlevelpoint = "Power User Bonus Points" (0x0C @48)
            public bool PuActive;         // powertimedate > now: PU vigente -> bônus de XP/gold
        }

        /// <summary>Carrega usergameinfo pela conta (name == id da conta).</summary>
        public async Task<GameInfo?> LoadGameInfoAsync(string accountName)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT id, name, charname, gold, ban, IFNULL(BanReason,''), powerlevelpoint, " +
                    "(powertimedate > NOW()) FROM usergameinfo WHERE name=@n LIMIT 1", c);
                cmd.Parameters.AddWithValue("@n", accountName);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return null;
                return new GameInfo
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    CharName = r.GetString(2),
                    Gold = r.GetInt32(3),
                    Ban = r.GetInt32(4) != 0,
                    BanReason = r.GetString(5),
                    PowerLevelPoint = r.GetInt32(6),
                    PuActive = !r.IsDBNull(7) && r.GetInt32(7) != 0,
                };
            }
            catch (Exception ex)
            {
                Log.Error("db", "LoadGameInfoAsync('{0}'): {1}", accountName, ex.Message);
                return null;
            }
        }

        /// <summary>Account-name (usergameinfo.name) do dono de um char, pelo nick. null se o char nao existe.
        /// Usado pelo messenger "add buddy": o cliente pede o account-id ao WORLD antes de adicionar (0x19 -> 0x0D).
        /// Replica DBCommandCharacterGetUserName @worldserv 0x413980 (JOIN characterinfo -> usergameinfo).</summary>
        public async Task<string?> GetCharOwnerByNickAsync(string nick)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT a.name FROM usergameinfo a JOIN characterinfo b ON a.id=b.userid " +
                    "WHERE b.name=@n LIMIT 1", c);
                cmd.Parameters.AddWithValue("@n", nick);
                return (await cmd.ExecuteScalarAsync()) as string;
            }
            catch (Exception ex) { Log.Error("db", "GetCharOwnerByNickAsync({0}): {1}", nick, ex.Message); return null; }
        }

        /// <summary>Persiste uma amizade na buddylist (Id=dono, Buddy=amigo; account-names) sem duplicar.
        /// Idempotente. Ver WorldServer.AddBuddyAsync — o AddBuddy do cliente e' mudo (mascara +0x140d4
        /// bit12=0 -> nao emite SVC_ADD_BUDDY), entao o world grava a amizade no 0x19.</summary>
        public async Task<bool> AddBuddyAsync(string ownerAccount, string buddyAccount)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "INSERT INTO buddylist (Id, Category, Buddy) SELECT @o, '', @b FROM DUAL " +
                    "WHERE NOT EXISTS (SELECT 1 FROM buddylist WHERE Id=@o AND Buddy=@b)", c);
                cmd.Parameters.AddWithValue("@o", ownerAccount);
                cmd.Parameters.AddWithValue("@b", buddyAccount);
                return await cmd.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex) { Log.Error("db", "AddBuddyAsync({0},{1}): {2}", ownerAccount, buddyAccount, ex.Message); return false; }
        }

        /// <summary>Messenger "nick change" (opcode 0x15 = CharacterChangeBuddyName): persiste o buddyname.
        /// Replica DBCommandCharacterChangeBuddyName @worldserv 0x4137a0 (UPDATE por usergameinfo.id).</summary>
        public async Task<bool> UpdateBuddyNameAsync(int gameInfoId, string buddyName)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("UPDATE usergameinfo SET buddyname=@bn WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@bn", buddyName);
                cmd.Parameters.AddWithValue("@id", gameInfoId);
                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex) { Log.Error("db", "UpdateBuddyNameAsync({0},{1}): {2}", gameInfoId, buddyName, ex.Message); return false; }
        }

        /// <summary>Grava/atualiza a sessao do messenger (account, ip) — o buddy resolve a identidade da
        /// conexao por IP. REPLACE: re-login do mesmo account sobrescreve. Ver BuddyDatabase.</summary>
        public async Task UpsertMessengerSessionAsync(string account, string ip)
        {
            if (string.IsNullOrEmpty(account)) return;
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "REPLACE INTO messenger_session (account, ip, login_ts) VALUES (@a, @ip, NOW())", c);
                cmd.Parameters.AddWithValue("@a", account);
                cmd.Parameters.AddWithValue("@ip", ip);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "UpsertMessengerSession({0}): {1}", account, ex.Message); }
        }

        /// <summary>Remove a sessao do messenger no logout do world.</summary>
        public async Task RemoveMessengerSessionAsync(string account)
        {
            if (string.IsNullOrEmpty(account)) return;
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("DELETE FROM messenger_session WHERE account=@a", c);
                cmd.Parameters.AddWithValue("@a", account);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "RemoveMessengerSession({0}): {1}", account, ex.Message); }
        }

        /// <summary>Registra a conexao do usuario (tabela loguserconnect).</summary>
        public async Task LogUserConnectAsync(int userId, string userName, int serverId, string ip)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "INSERT INTO loguserconnect (userid, username, serverid, RealIP, userip, connecttime) " +
                    "VALUES (@uid, @uname, @sid, @ip, @ip, NOW())", c);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@uname", userName);
                cmd.Parameters.AddWithValue("@sid", serverId);
                cmd.Parameters.AddWithValue("@ip", ip);
                await cmd.ExecuteNonQueryAsync();
                Log.Debug("db", "loguserconnect: uid={0} '{1}' @ {2}", userId, userName, ip);
            }
            catch (Exception ex)
            {
                Log.Error("db", "LogUserConnectAsync: {0}", ex.Message);
            }
        }

    }
}
