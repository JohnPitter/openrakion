using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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
        private readonly string _userConn;

        public WorldDatabase(WorldConfig.DbConfig cfg, WorldConfig.DbConfig? userCfg = null)
        {
            _conn = cfg.ConnectionString;
            _userConn = (userCfg ?? cfg).ConnectionString;
        }

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

        /// <summary>
        /// Provisiona o que o dump v258 nao tem mas o servidor offline usa. Idempotente (IF NOT EXISTS)
        /// — roda no boot p/ sobreviver a um re-import do dump. `useriteminfo` é a fonte canônica de
        /// storage/equipamento; `pu_config` guarda preço, bônus e multiplicadores do Power User.
        /// </summary>
        public async Task EnsureSchemaAsync()
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await Exec(c, "ALTER TABLE itembox ADD COLUMN IF NOT EXISTS qslot TINYINT NOT NULL DEFAULT 0");
                await Exec(c, "ALTER TABLE itembox ADD COLUMN IF NOT EXISTS level TINYINT NOT NULL DEFAULT 0");
                await Exec(c, "ALTER TABLE itembox ADD COLUMN IF NOT EXISTS boxslot SMALLINT NULL");
                await Exec(c, "ALTER TABLE usergameinfo MODIFY stagelevelfree BIGINT NOT NULL DEFAULT 0");
                await Exec(c, "ALTER TABLE characterinfo MODIFY name VARCHAR(12) NOT NULL");
                await EnsureInnoDbAsync(c, "characterinfo");
                await EnsureInnoDbAsync(c, "usergameinfo");
                await EnsureInnoDbAsync(c, "useriteminfo");
                await EnsureInnoDbAsync(c, "itembox");
                await EnsureInnoDbAsync(c, "userstageinfo");
                await EnsureInnoDbAsync(c, "cash");
                await EnsureInnoDbAsync(c, "couponinfo");
                await EnsureInnoDbAsync(c, "logcoupon");
                await EnsureInnoDbAsync(c, "logcharstateclear");
                await EnsureInnoDbAsync(c, "logchangecharname");
                await EnsureInnoDbAsync(c, "logbuycashitem");
                await EnsureInnoDbAsync(c, "logbuypoweruser");
                await EnsureInnoDbAsync(c, "loguseritem");
                await EnsureInnoDbAsync(c, "pendingpresents");
                await EnsureInnoDbAsync(c, "logpresent");
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS logdeletecharacter (" +
                    "id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY," +
                    "userid INT NOT NULL,charname VARCHAR(11) NOT NULL," +
                    "deletetime DATETIME(6) NOT NULL,level TINYINT UNSIGNED NOT NULL," +
                    "mode TINYINT UNSIGNED NOT NULL," +
                    "INDEX ix_logdeletecharacter_user(userid,deletetime)) ENGINE=InnoDB");
                await Exec(c, "ALTER TABLE logdeletecharacter ADD COLUMN IF NOT EXISTS " +
                    "mode TINYINT UNSIGNED NOT NULL DEFAULT 0");
                await Exec(c, "ALTER TABLE logdeletecharacter MODIFY charname VARCHAR(12) NOT NULL");
                await Exec(c, "ALTER TABLE logdeletecharacter ADD INDEX IF NOT EXISTS " +
                    "ix_logdeletecharacter_user (userid,deletetime)");
                await EnsureInnoDbAsync(c, "logdeletecharacter");
                await EnsureLogIdentityAsync(c, "logdeletecharacter");
                await EnsureInnoDbAsync(c, "lotto");
                await MigrateLegacyItemBoxAsync(c);
                await NormalizeItemSerialsAsync(c);
                await EnsureItemSerialIndexAsync(c);
                await EnsureLogIdentityAsync(c, "logcoupon");
                await EnsureLogIdentityAsync(c, "logcharstateclear");
                await EnsureLogIdentityAsync(c, "logchangecharname");
                await Exec(c, "ALTER TABLE logchangecharname MODIFY charname_prev VARCHAR(12) NOT NULL");
                await EnsureLogIdentityAsync(c, "logbuycashitem");
                await EnsureLogIdentityAsync(c, "logbuypoweruser");
                await EnsureLogIdentityAsync(c, "loguseritem");
                await Exec(c, "ALTER TABLE usergameinfo ADD INDEX IF NOT EXISTS ix_usergameinfo_buddyname (buddyname)");
                await Exec(c, "ALTER TABLE characterinfo ADD UNIQUE INDEX IF NOT EXISTS ux_characterinfo_name (name)");
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS pu_config (" +
                    " id TINYINT NOT NULL PRIMARY KEY," +
                    " price INT NOT NULL DEFAULT 8000," +
                    " renewal_price INT NOT NULL DEFAULT 6000," +
                    " bonus_points SMALLINT NOT NULL DEFAULT 5," +
                    " duration_days SMALLINT NOT NULL DEFAULT 30," +
                    " exp_mult DECIMAL(4,2) NOT NULL DEFAULT 1.50," +
                    " gold_mult DECIMAL(4,2) NOT NULL DEFAULT 1.00," +
                    " promo_active TINYINT(1) NOT NULL DEFAULT 0," +
                    " promo_exp_mult DECIMAL(4,2) NOT NULL DEFAULT 2.00," +
                    " promo_gold_mult DECIMAL(4,2) NOT NULL DEFAULT 1.00," +
                    " promo_start DATETIME NULL," +
                    " promo_end DATETIME NULL)");
                await Exec(c, "INSERT IGNORE INTO pu_config (id) VALUES (1)");
                await Exec(c, "ALTER TABLE pu_config ADD COLUMN IF NOT EXISTS " +
                    "renewal_price INT NOT NULL DEFAULT 6000 AFTER price");
                await Exec(c, "ALTER TABLE pu_config ADD COLUMN IF NOT EXISTS " +
                    "config_version TINYINT UNSIGNED NOT NULL DEFAULT 0");
                await Exec(c, "UPDATE pu_config SET bonus_points=5,config_version=2 " +
                    "WHERE id=1 AND config_version<2");
                await Exec(c, "ALTER TABLE pu_config MODIFY gold_mult DECIMAL(4,2) NOT NULL DEFAULT 1.00");
                await Exec(c, "ALTER TABLE pu_config MODIFY promo_gold_mult DECIMAL(4,2) NOT NULL DEFAULT 1.00");
                await Exec(c, "UPDATE pu_config SET " +
                    "gold_mult=IF(gold_mult=1.50,1.00,gold_mult)," +
                    "promo_gold_mult=IF(promo_gold_mult=2.00,1.00,promo_gold_mult)," +
                    "config_version=3 WHERE id=1 AND config_version<3");
                // Refino configurável: coeficientes por catalisador (linha por catalisador) + globais (singleton id=1).
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS enchant_catalyzer (" +
                    " catalyzer_id INT NOT NULL PRIMARY KEY," +
                    " name VARCHAR(32) NOT NULL DEFAULT ''," +
                    " base_success DECIMAL(4,3) NOT NULL DEFAULT 0.900," +
                    " decay DECIMAL(4,3) NOT NULL DEFAULT 0.070," +
                    " level_cap TINYINT NOT NULL DEFAULT 14)");
                await Exec(c,
                    "INSERT IGNORE INTO enchant_catalyzer (catalyzer_id,name,base_success,decay,level_cap) VALUES" +
                    " (13001,'Mithril',0.700,0.060,4)," +
                    " (13002,'Adamantium',0.850,0.060,14)," +
                    " (13003,'Orehalcon',0.900,0.050,14)," +
                    " (13004,'test+1',0.750,0.040,9)," +
                    " (13005,'test+2',0.950,0.030,14)");
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS enchant_config (" +
                    " id TINYINT NOT NULL PRIMARY KEY," +
                    " jewel_floor DECIMAL(4,3) NOT NULL DEFAULT 0.050," +
                    " jewel_bonus DECIMAL(4,3) NOT NULL DEFAULT 0.030," +
                    " event_mult DECIMAL(4,2) NOT NULL DEFAULT 1.00," +
                    " pu_mult DECIMAL(4,2) NOT NULL DEFAULT 1.00," +
                    " floor_min DECIMAL(4,3) NOT NULL DEFAULT 0.050," +
                    " ceil_max DECIMAL(4,3) NOT NULL DEFAULT 0.980," +
                    " downgrade_lo DECIMAL(4,3) NOT NULL DEFAULT 0.120," +
                    " downgrade_hi DECIMAL(4,3) NOT NULL DEFAULT 0.300)");
                await Exec(c, "INSERT IGNORE INTO enchant_config (id) VALUES (1)");
                await EnsureEnchantLedgerSchemaAsync(c);
                await EnsureChatSchemaAsync(c);
                await EnsureLauncherTicketSchemaAsync(c);
                await EnsureMatchSettlementSchemaAsync(c);
                await EnsureGamePointSettlementSchemaAsync(c);
                await EnsureStageSettlementSchemaAsync(c);
                Log.Ok("db", "schema verificado (inventário, economia, chat, launcher e settlements)");
            }
            catch (Exception ex) { Log.Error("db", "EnsureSchemaAsync: {0}", ex.Message); }
        }

        private static async Task Exec(MySqlConnection c, string sql)
        {
            await using var cmd = new MySqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task EnsureChatSchemaAsync(MySqlConnection connection)
        {
            await Exec(connection,
                "CREATE TABLE IF NOT EXISTS chat_mute (" +
                "account_id VARCHAR(16) NOT NULL PRIMARY KEY,muted_until DATETIME(6) NOT NULL," +
                "reason VARCHAR(100) NOT NULL,operator_id VARCHAR(64) NOT NULL," +
                "updated_at DATETIME(6) NOT NULL) ENGINE=InnoDB");
            await Exec(connection,
                "CREATE TABLE IF NOT EXISTS chat_block (" +
                "owner_account_id VARCHAR(16) NOT NULL,blocked_account_id VARCHAR(16) NOT NULL," +
                "created_at DATETIME(6) NOT NULL," +
                "PRIMARY KEY(owner_account_id,blocked_account_id)) ENGINE=InnoDB");
            await Exec(connection,
                "CREATE TABLE IF NOT EXISTS chat_moderation_log (" +
                "id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,sender_account VARCHAR(16) NOT NULL," +
                "target_account VARCHAR(16) NOT NULL,scope TINYINT UNSIGNED NOT NULL," +
                "action TINYINT UNSIGNED NOT NULL,rule_id VARCHAR(100) NOT NULL," +
                "text_hash CHAR(64) NOT NULL,length_before SMALLINT UNSIGNED NOT NULL," +
                "length_after SMALLINT UNSIGNED NOT NULL,created_at DATETIME(6) NOT NULL," +
                "INDEX ix_chat_moderation_sender(sender_account,created_at)) ENGINE=InnoDB");
        }

        private static async Task EnsureLauncherTicketSchemaAsync(MySqlConnection connection)
        {
            await Exec(connection, LauncherTicketSchema.CreateSql);
            foreach (string sql in LauncherTicketSchema.MigrationSql)
                await Exec(connection, sql);
        }

        private static async Task EnsureInnoDbAsync(MySqlConnection c, string table)
        {
            await using var check = new MySqlCommand(
                "SELECT ENGINE FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table", c);
            check.Parameters.AddWithValue("@table", table);
            string? engine = Convert.ToString(await check.ExecuteScalarAsync());
            if (string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase)) return;
            await Exec(c, $"ALTER TABLE `{table}` ENGINE=InnoDB");
        }

        private static async Task EnsureLogIdentityAsync(MySqlConnection c, string table)
        {
            await using var check = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.columns " +
                "WHERE table_schema=DATABASE() AND table_name=@table AND column_name='id'", c);
            check.Parameters.AddWithValue("@table", table);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) != 0) return;

            await using var primary = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.table_constraints " +
                "WHERE table_schema=DATABASE() AND table_name=@table AND constraint_type='PRIMARY KEY'", c);
            primary.Parameters.AddWithValue("@table", table);
            bool hasPrimaryKey = Convert.ToInt32(await primary.ExecuteScalarAsync()) != 0;
            string dropPrimary = hasPrimaryKey ? "DROP PRIMARY KEY, " : "";
            await Exec(c, $"ALTER TABLE `{table}` {dropPrimary}ADD COLUMN id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY FIRST");
        }

        private static async Task EnsureItemSerialIndexAsync(MySqlConnection connection)
        {
            const string indexName = "ux_useriteminfo_item_sn";
            var columns = new List<string>();
            await using (var command = new MySqlCommand(
                "SELECT column_name FROM information_schema.statistics " +
                "WHERE table_schema=DATABASE() AND table_name='useriteminfo' " +
                "AND index_name=@index ORDER BY seq_in_index", connection))
            {
                command.Parameters.AddWithValue("@index", indexName);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
            }
            if (columns.Count == 2 &&
                string.Equals(columns[0], "sn_type", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(columns[1], "item_sn", StringComparison.OrdinalIgnoreCase))
                return;
            if (columns.Count > 0)
                await Exec(connection, $"ALTER TABLE useriteminfo DROP INDEX `{indexName}`");
            await Exec(connection, "ALTER TABLE useriteminfo ADD UNIQUE INDEX " +
                "ux_useriteminfo_item_sn (sn_type,item_sn)");
        }

        /// <summary>Carrega a config do Power User (pu_config id=1). Default se a linha faltar.</summary>
        public async Task<PuConfig> LoadPuConfigAsync()
        {
            var cfg = new PuConfig();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT price,renewal_price,bonus_points,duration_days,exp_mult,gold_mult,promo_active," +
                    "promo_exp_mult,promo_gold_mult,promo_start,promo_end FROM pu_config WHERE id=1", c);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    cfg.Price = r.GetInt32(0);
                    cfg.RenewalPrice = r.GetInt32(1);
                    cfg.BonusPoints = r.GetInt32(2);
                    cfg.DurationDays = r.GetInt32(3);
                    cfg.ExpMult = (double)r.GetDecimal(4);
                    cfg.GoldMult = (double)r.GetDecimal(5);
                    cfg.PromoActive = r.GetInt32(6) != 0;
                    cfg.PromoExpMult = (double)r.GetDecimal(7);
                    cfg.PromoGoldMult = (double)r.GetDecimal(8);
                    cfg.PromoStart = r.IsDBNull(9) ? null : r.GetDateTime(9);
                    cfg.PromoEnd = r.IsDBNull(10) ? null : r.GetDateTime(10);
                }
            }
            catch (Exception ex) { Log.Error("db", "LoadPuConfigAsync: {0}", ex.Message); }
            return cfg;
        }

        /// <summary>Carrega a config do refino: coeficientes por catalisador (enchant_catalyzer) + globais
        /// (enchant_config id=1). Defaults se as linhas faltarem; o seed do EnsureSchemaAsync garante as 5
        /// linhas de catalisador.</summary>
        public async Task<EnchantConfig> LoadEnchantConfigAsync()
        {
            var cfg = new EnchantConfig();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using (var cmd = new MySqlCommand(
                    "SELECT catalyzer_id,base_success,decay,level_cap FROM enchant_catalyzer", c))
                await using (var r = await cmd.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        cfg.SetCatalyzer(r.GetInt32(0), new EnchantConfig.Catalyzer(
                            (double)r.GetDecimal(1), (double)r.GetDecimal(2), Convert.ToInt32(r.GetValue(3))));
                await using (var cmd = new MySqlCommand(
                    "SELECT jewel_floor,jewel_bonus,event_mult,pu_mult,floor_min,ceil_max,downgrade_lo,downgrade_hi,config_version,original_outcomes" +
                    " FROM enchant_config WHERE id=1", c))
                await using (var r = await cmd.ExecuteReaderAsync())
                    if (await r.ReadAsync())
                    {
                        cfg.JewelFloor = (double)r.GetDecimal(0);
                        cfg.JewelBonus = (double)r.GetDecimal(1);
                        cfg.EventMult = (double)r.GetDecimal(2);
                        cfg.PuMult = (double)r.GetDecimal(3);
                        cfg.FloorMin = (double)r.GetDecimal(4);
                        cfg.CeilMax = (double)r.GetDecimal(5);
                        cfg.DowngradeLo = (double)r.GetDecimal(6);
                        cfg.DowngradeHi = (double)r.GetDecimal(7);
                        cfg.Version = r.GetInt32(8);
                        cfg.UseOriginalOutcomes = r.GetBoolean(9);
                    }
            }
            catch (Exception ex) { Log.Error("db", "LoadEnchantConfigAsync: {0}", ex.Message); }
            return cfg;
        }

        public sealed class Account
        {
            public string Id = "";
            public int Authority;
            public int Country;
            public bool Banned;
        }

        public async Task<Account?> AuthenticateCredentialAsync(
            string id, string credential, bool allowPasswordLogin,
            LauncherBuildIdentity? requiredBuild = null)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                if (LauncherTicketToken.IsValidFormat(credential))
                {
                    Account? ticketAccount = await ConsumeTicketAsync(
                        c, id, credential, requiredBuild);
                    if (ticketAccount != null) return ticketAccount;
                }
                if (!allowPasswordLogin) return null;
                return await AuthenticatePasswordAsync(c, id, credential);
            }
            catch (Exception ex)
            {
                Log.Error("db", "AuthenticateCredentialAsync('{0}'): {1}", id, ex.Message);
                return null;
            }
        }

        public Task<Account?> AuthenticateAsync(string id, string password) =>
            AuthenticateCredentialAsync(id, password, allowPasswordLogin: true);

        private static async Task<Account?> ConsumeTicketAsync(
            MySqlConnection connection, string id, string ticket,
            LauncherBuildIdentity? requiredBuild)
        {
            LauncherBuildIdentity build = requiredBuild ?? default;
            await using var transaction = await connection.BeginTransactionAsync();
            await using var consume = new MySqlCommand(
                "UPDATE launcher_ticket SET used_at=UTC_TIMESTAMP(6) " +
                "WHERE token_hash=@hash AND account_id=@id AND used_at IS NULL " +
                "AND expires_at>UTC_TIMESTAMP(6) " +
                "AND (@app=0 OR (app_id=@app AND build_version=@build))",
                connection, transaction);
            consume.Parameters.Add("@hash", MySqlDbType.Binary, 32).Value =
                LauncherTicketToken.Hash(ticket);
            consume.Parameters.AddWithValue("@id", id);
            consume.Parameters.AddWithValue("@app", build.AppId);
            consume.Parameters.AddWithValue("@build", build.BuildVersion);
            if (await consume.ExecuteNonQueryAsync() != 1) return null;

            Account? account = await ReadAccountAsync(connection, transaction, id, password: null);
            if (account is null) return null;
            await transaction.CommitAsync();
            return account;
        }

        private static async Task<Account?> AuthenticatePasswordAsync(
            MySqlConnection connection, string id, string password) =>
            await ReadAccountAsync(connection, transaction: null, id, password);

        private static async Task<Account?> ReadAccountAsync(
            MySqlConnection connection, MySqlTransaction? transaction,
            string id, string? password)
        {
            const string sql = "SELECT password,Authority,country FROM user WHERE id=@id";
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@id", id);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            if (password != null && !PasswordsEqual(reader.GetString(0), password))
                return null;
            return new Account
            {
                Id = id,
                Authority = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                Country = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            };
        }

        private static bool PasswordsEqual(string expected, string supplied)
        {
            byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
            byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied);
            return expectedBytes.Length == suppliedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        }

        public sealed class GameInfo
        {
            public int Id;
            public string Name = "";
            public string CharName = "";
            public string BuddyName = "";
            public bool TutorialClear;
            public int Gold;
            public bool Ban;
            public string BanReason = "";
            public int PowerLevelPoint;   // usergameinfo.powerlevelpoint = "Power User Bonus Points" (0x0C @48)
            public uint PowerTimeMarker;
            public uint CurrentMinuteMarker;
            public bool PuActive;         // powertimedate > now: PU vigente -> bônus de XP/gold
            public DateTime? PuExpiresAt;
            public byte Bag = 1;
            public byte CharacterSlots = 4;
            public uint StageLevelFreeMarker;
            public int ClanId;
        }

        /// <summary>Carrega usergameinfo pela conta (name == id da conta).</summary>
        public async Task<GameInfo?> LoadGameInfoAsync(string accountName)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT id,name,charname,buddyname,tutorial,gold,ban,IFNULL(BanReason,'')," +
                    "powerlevelpoint,powertime," +
                    "NULLIF(powertimedate,'0000-00-00 00:00:00')," +
                    "(powertimedate > NOW()),bag,slot,stagelevelfree,clanid," +
                    "(TO_DAYS(NOW())*1440+HOUR(NOW())*60+MINUTE(NOW())) " +
                    "FROM usergameinfo WHERE name=@n LIMIT 1", c);
                cmd.Parameters.AddWithValue("@n", accountName);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return null;
                return new GameInfo
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    CharName = r.GetString(2),
                    BuddyName = r.GetString(3),
                    TutorialClear = r.GetInt32(4) != 0,
                    Gold = r.GetInt32(5),
                    Ban = r.GetInt32(6) != 0,
                    BanReason = r.GetString(7),
                    PowerLevelPoint = r.GetInt32(8),
                    PowerTimeMarker = checked((uint)Math.Max(0, r.GetInt64(9))),
                    PuExpiresAt = r.IsDBNull(10) ? null : r.GetDateTime(10),
                    PuActive = !r.IsDBNull(11) && r.GetInt32(11) != 0,
                    Bag = checked((byte)r.GetInt32(12)),
                    CharacterSlots = checked((byte)r.GetInt32(13)),
                    StageLevelFreeMarker = checked((uint)r.GetInt64(14)),
                    ClanId = r.IsDBNull(15) ? 0 : r.GetInt32(15),
                    CurrentMinuteMarker = checked((uint)r.GetInt64(16)),
                };
            }
            catch (Exception ex)
            {
                Log.Error("db", "LoadGameInfoAsync('{0}'): {1}", accountName, ex.Message);
                return null;
            }
        }

        // ---- repositorio de jogo (personagens, itens, cash, cla) ----

        /// <summary>Itens de um personagem (useriteminfo.characterid).</summary>
        public async Task<List<UserItem>> LoadItemsAsync(int characterId)
        {
            var list = new List<UserItem>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT id,userid,characterid,itemid,item_sn,sn_type,level,limittime,slot,exp " +
                    "FROM useriteminfo WHERE characterid=@c AND " +
                    "(limittime=0 OR limittime>=((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW())))", c);
                cmd.Parameters.AddWithValue("@c", characterId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    list.Add(new UserItem
                    {
                        Id = r.GetInt32(0),
                        UserId = r.GetInt32(1),
                        CharacterId = r.GetInt32(2),
                        ItemId = r.GetInt32(3),
                        ItemSn = r.GetInt32(4),
                        SnType = (byte)r.GetInt32(5),
                        Level = (byte)r.GetInt32(6),
                        LimitTime = r.GetInt32(7),
                        Slot = (byte)r.GetInt32(8),
                        Exp = r.GetInt64(9),
                    });
            }
            catch (Exception ex) { Log.Error("db", "LoadItemsAsync({0}): {1}", characterId, ex.Message); }
            return list;
        }

        /// <summary>Cash (pontos pagos) de uma conta (cash.id char(16)).</summary>
        public async Task<int> GetCashAsync(string accountId)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT cash FROM cash WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@id", accountId);
                object? v = await cmd.ExecuteScalarAsync();
                return v == null || v is DBNull ? 0 : Convert.ToInt32(v);
            }
            catch (Exception ex) { Log.Error("db", "GetCashAsync({0}): {1}", accountId, ex.Message); return 0; }
        }

        /// <summary>Ajusta o gold de uma conta (usergameinfo.id). delta pode ser negativo.</summary>
        public async Task AddGoldAsync(int usergameinfoId, int delta)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "UPDATE usergameinfo SET gold=GREATEST(0, gold+@d) WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@d", delta);
                cmd.Parameters.AddWithValue("@id", usergameinfoId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "AddGoldAsync({0},{1}): {2}", usergameinfoId, delta, ex.Message); }
        }

        /// <summary>Catalogo de itens (iteminfo) — carregado uma vez no boot.</summary>
        public async Task<List<ItemDef>> LoadItemDefsAsync()
        {
            var list = new List<ItemDef>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT id,type,Class,level,shop,gold,cash,hit1,hit2,hit3,hit4,chit,ap,hp,maxcp,power FROM iteminfo", c);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    list.Add(new ItemDef
                    {
                        Id = r.GetInt32(0),
                        Type = (byte)r.GetInt32(1),
                        Class = (byte)r.GetInt32(2),
                        Level = (byte)r.GetInt32(3),
                        Shop = (byte)r.GetInt32(4),
                        Gold = r.GetInt32(5),
                        Cash = r.GetInt32(6),
                        Hit1 = r.GetInt32(7),
                        Hit2 = r.GetInt32(8),
                        Hit3 = r.GetInt32(9),
                        Hit4 = r.GetInt32(10),
                        CHit = r.GetInt32(11),
                        Ap = r.GetInt32(12),
                        Hp = r.GetInt32(13),
                        MaxCp = r.GetInt32(14),
                        Power = r.GetInt32(15),
                    });
            }
            catch (Exception ex) { Log.Error("db", "LoadItemDefsAsync: {0}", ex.Message); }
            return list;
        }

        /// <summary>
        /// Credita o resultado de partida no personagem (characterinfo): incrementos de
        /// win/lose/draw (liquidacao do match) e/ou exp (reporte 0x50/0x53).
        /// </summary>
        public async Task AddCharacterResultAsync(int characterId, int win, int lose, int draw, long exp)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "UPDATE characterinfo SET win=win+@w, lose=lose+@l, draw=draw+@d, exp=exp+@e WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@w", win);
                cmd.Parameters.AddWithValue("@l", lose);
                cmd.Parameters.AddWithValue("@d", draw);
                cmd.Parameters.AddWithValue("@e", exp);
                cmd.Parameters.AddWithValue("@id", characterId);
                await cmd.ExecuteNonQueryAsync();
                Log.Debug("db", "AddCharacterResult: char={0} w/l/d=+{1}/+{2}/+{3} exp=+{4}", characterId, win, lose, draw, exp);
            }
            catch (Exception ex) { Log.Error("db", "AddCharacterResultAsync({0}): {1}", characterId, ex.Message); }
        }

        /// <summary>Carrega os ranks de stage do char (userstageinfo) num array indexado por stage (0=sem rank,
        /// 1=D..5=S). Vai no overlay do 0x0C@333 (1 byte/stage) -> "RANK X CLEAR"/Last Rank na seleção de stages.</summary>
        public async Task<byte[]> LoadStageRanksAsync(int characterId)
        {
            var arr = new byte[100];
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT stage,`rank` FROM userstageinfo WHERE characterid=@cid", c);
                cmd.Parameters.AddWithValue("@cid", characterId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    int stage = r.GetByte(0);
                    if (stage >= 1 && stage < arr.Length) arr[stage] = (byte)Math.Clamp(r.GetInt32(1), 0, 255);
                }
            }
            catch (Exception ex) { Log.Error("db", "LoadStageRanksAsync({0}): {1}", characterId, ex.Message); }
            return arr;
        }

        /// <summary>
        /// Curva de level (classlevelinfo): exp TOTAL necessario p/ avancar de cada nivel,
        /// chaveada por (classe, nivel). Carregada 1x no boot (como o catalogo de itens).
        /// </summary>
        public async Task<Dictionary<(byte Cls, byte Level), int>> LoadLevelCurveAsync()
        {
            var map = new Dictionary<(byte, byte), int>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT Class, level, exp FROM classlevelinfo", c);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    map[((byte)r.GetInt16(0), (byte)r.GetInt16(1))] = r.GetInt32(2);
            }
            catch (Exception ex) { Log.Error("db", "LoadLevelCurveAsync: {0}", ex.Message); }
            return map;
        }

        /// <summary>Grava nivel/levelpoint do personagem (level-up server-side).</summary>
        public async Task UpdateCharacterLevelAsync(int characterId, byte level, byte levelPoint)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "UPDATE characterinfo SET level=@lv, levelpoint=@lp WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@lv", level);
                cmd.Parameters.AddWithValue("@lp", levelPoint);
                cmd.Parameters.AddWithValue("@id", characterId);
                await cmd.ExecuteNonQueryAsync();
                Log.Ok("db", "UpdateCharacterLevel: char={0} level={1} levelpoint={2}", characterId, level, levelPoint);
            }
            catch (Exception ex) { Log.Error("db", "UpdateCharacterLevelAsync({0}): {1}", characterId, ex.Message); }
        }

        // Colunas do char p/ o char-select (0x0C) — UMA fonte (ordem = índices em MapCharacter).
        private const string CharSelectColumns =
            "id,userid,name,used,auth,Class,level,win,lose,draw,exp,levelpoint,slot," +
            "hit1,hit2,hit3,hit4,chit,hp,ap,attackspeed,speed,maxcp,rankgrade," +
            "totalrank,classrank,potionslot";

        private static CharacterInfo MapCharacter(System.Data.Common.DbDataReader r) => new CharacterInfo
        {
            Id = r.GetInt32(0),
            UserId = r.GetInt32(1),
            Name = r.GetString(2),
            Used = r.GetInt32(3) != 0,
            Auth = (byte)r.GetInt32(4),
            Class = (byte)r.GetInt32(5),
            Level = (byte)r.GetInt32(6),
            Win = r.GetInt32(7),
            Lose = r.GetInt32(8),
            Draw = r.GetInt32(9),
            Exp = r.GetInt32(10),
            LevelPoint = (byte)r.GetInt32(11),
            Slot = (byte)r.GetInt32(12),
            Hit1 = (byte)r.GetInt32(13),
            Hit2 = (byte)r.GetInt32(14),
            Hit3 = (byte)r.GetInt32(15),
            Hit4 = (byte)r.GetInt32(16),
            Chit = (byte)r.GetInt32(17),
            Hp = (byte)r.GetInt32(18),
            Ap = (byte)r.GetInt32(19),
            AttackSpeed = (byte)r.GetInt32(20),
            Speed = (byte)r.GetInt32(21),
            Maxcp = (byte)r.GetInt32(22),
            RankGrade = checked((byte)r.GetInt32(23)),
            TotalRank = r.GetInt32(24),
            ClassRank = r.GetInt32(25),
            PotionSlots = checked((byte)r.GetInt32(26)),
        };

        /// <summary>Char selecionado em usergameinfo.charname; fallback pelo menor slot habilitado.</summary>
        public async Task<CharacterInfo?> LoadActiveCharacterAsync(int userId)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT " + CharSelectColumns + " FROM characterinfo WHERE userid=@u AND auth<>10 " +
                    "ORDER BY (name=(SELECT charname FROM usergameinfo WHERE id=@u)) DESC, slot ASC LIMIT 1", c);
                cmd.Parameters.AddWithValue("@u", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                return await r.ReadAsync() ? MapCharacter(r) : null;
            }
            catch (Exception ex) { Log.Error("db", "LoadActiveCharacterAsync({0}): {1}", userId, ex.Message); return null; }
        }

        /// <summary>Todos os chars criados da conta (ordem de slot) — p/ a lista do char-select (0x0C).</summary>
        public async Task<List<CharacterInfo>> LoadCharactersAsync(int userId)
        {
            var list = new List<CharacterInfo>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT " + CharSelectColumns + " FROM characterinfo WHERE userid=@u AND auth<>10 ORDER BY slot ASC", c);
                cmd.Parameters.AddWithValue("@u", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add(MapCharacter(r));
            }
            catch (Exception ex) { Log.Error("db", "LoadCharactersAsync({0}): {1}", userId, ex.Message); }
            return list;
        }

        public async Task<BuddyNameChangeResult> ChangeBuddyNameAsync(int userId, string buddyName)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                await using (var duplicate = new MySqlCommand(
                    "SELECT id FROM usergameinfo WHERE buddyname=@name AND id<>@id LIMIT 1 FOR UPDATE", c, tx))
                {
                    duplicate.Parameters.AddWithValue("@name", buddyName);
                    duplicate.Parameters.AddWithValue("@id", userId);
                    if (await duplicate.ExecuteScalarAsync() != null)
                    { await tx.RollbackAsync(); return BuddyNameChangeResult.Duplicate; }
                }

                await using var update = new MySqlCommand(
                    "UPDATE usergameinfo SET buddyname=@name WHERE id=@id", c, tx);
                update.Parameters.AddWithValue("@name", buddyName);
                update.Parameters.AddWithValue("@id", userId);
                int changed = await update.ExecuteNonQueryAsync();
                if (changed == 0) { await tx.RollbackAsync(); return BuddyNameChangeResult.NotFound; }
                await tx.CommitAsync();
                return BuddyNameChangeResult.Success;
            }
            catch (Exception ex)
            {
                Log.Error("db", "ChangeBuddyNameAsync(user={0}): {1}", userId, ex.Message);
                return BuddyNameChangeResult.Failed;
            }
        }

        public async Task<bool> MarkTutorialClearAsync(int userId)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "UPDATE usergameinfo SET tutorial=1 WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@id", userId);
                if (await cmd.ExecuteNonQueryAsync() > 0) return true;
                await using var exists = new MySqlCommand(
                    "SELECT 1 FROM usergameinfo WHERE id=@id LIMIT 1", c);
                exists.Parameters.AddWithValue("@id", userId);
                return await exists.ExecuteScalarAsync() != null;
            }
            catch (Exception ex)
            {
                Log.Error("db", "MarkTutorialClearAsync(user={0}): {1}", userId, ex.Message);
                return false;
            }
        }

        public async Task<CharacterDeleteOutcome> DeleteCharacterAsync(
            int userId, int characterId, string deleteKey)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                CharacterDeleteRow? row = await LoadCharacterDeleteRowAsync(
                    c, tx, userId, characterId);

                if (row == null)
                {
                    await tx.RollbackAsync();
                    return new CharacterDeleteOutcome(CharacterDeleteResult.NotFound);
                }

                var target = new CharacterDeleteTarget(userId, characterId, row);
                var context = new Domain.CharacterDeleteContext(
                    row.Level, row.Used, row.AgeDays,
                    string.Equals(row.Name, row.ActiveName, StringComparison.OrdinalIgnoreCase),
                    row.KeyIsRecent, row.StoredKey);
                var decision = Domain.CharacterDeletePolicy.Evaluate(context, deleteKey);
                if (decision.Action == Domain.CharacterDeleteAction.Reject)
                {
                    await tx.RollbackAsync();
                    return new CharacterDeleteOutcome(decision.Result);
                }

                if (decision.Action == Domain.CharacterDeleteAction.IssueKey)
                    return await IssueCharacterDeleteKeyAsync(c, tx, target);

                if (decision.Action == Domain.CharacterDeleteAction.HardDelete)
                    await HardDeleteCharacterAsync(c, tx, target);
                else
                    await SoftDeleteCharacterAsync(c, tx, target);

                await InsertCharacterDeleteLogAsync(c, tx, target,
                    decision.Action == Domain.CharacterDeleteAction.HardDelete ? (byte)0 : (byte)1);
                await tx.CommitAsync();
                return new CharacterDeleteOutcome(CharacterDeleteResult.Success);
            }
            catch (Exception ex)
            {
                Log.Error("db", "DeleteCharacterAsync(user={0}, char={1}): {2}", userId, characterId, ex.Message);
                return new CharacterDeleteOutcome(CharacterDeleteResult.Failed);
            }
        }

        public async Task<bool> RevokeCharacterDeleteKeyAsync(
            int userId, int characterId, string expectedKey)
        {
            if (userId <= 0 || characterId <= 0 || expectedKey.Length == 0) return false;
            try
            {
                await using var connection = new MySqlConnection(_conn);
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    "UPDATE characterinfo SET deletekey=''," +
                    "changetime=DATE_SUB(NOW(),INTERVAL 2 HOUR) " +
                    "WHERE userid=@u AND id=@id AND auth<>10 AND deletekey=@key",
                    connection);
                command.Parameters.AddWithValue("@u", userId);
                command.Parameters.AddWithValue("@id", characterId);
                command.Parameters.AddWithValue("@key", expectedKey);
                return await command.ExecuteNonQueryAsync() == 1;
            }
            catch (Exception ex)
            {
                Log.Error("db", "RevokeCharacterDeleteKeyAsync(user={0}, char={1}): {2}",
                    userId, characterId, ex.Message);
                return false;
            }
        }

        private static async Task<CharacterDeleteRow?> LoadCharacterDeleteRowAsync(
            MySqlConnection connection, MySqlTransaction transaction, int userId, int characterId)
        {
            await using var command = new MySqlCommand(
                "SELECT c.name,c.level,c.used," +
                "TO_DAYS(NOW())-IFNULL(TO_DAYS(c.createtime),0)," +
                "g.charname,g.name,c.deletekey,(SUBDATE(NOW(),INTERVAL 1 HOUR)<c.changetime) " +
                "FROM characterinfo c JOIN usergameinfo g ON g.id=c.userid " +
                "WHERE c.userid=@u AND c.id=@id AND c.auth<>10 LIMIT 1 FOR UPDATE",
                connection, transaction);
            command.Parameters.AddWithValue("@u", userId);
            command.Parameters.AddWithValue("@id", characterId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return new CharacterDeleteRow(
                reader.GetString(0), reader.GetByte(1), reader.GetBoolean(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetBoolean(7));
        }

        private async Task<CharacterDeleteOutcome> IssueCharacterDeleteKeyAsync(
            MySqlConnection connection, MySqlTransaction transaction, CharacterDeleteTarget target)
        {
            string email = await FindCharacterDeleteEmailAsync(target.Row.AccountName);
            if (email.Length == 0)
            {
                await transaction.RollbackAsync();
                return new CharacterDeleteOutcome(CharacterDeleteResult.InvalidEmail);
            }

            string key = CreateCharacterDeleteKey();
            await using var update = new MySqlCommand(
                "UPDATE characterinfo SET deletekey=@key,changetime=NOW() " +
                "WHERE userid=@u AND id=@id AND auth<>10", connection, transaction);
            update.Parameters.AddWithValue("@key", key);
            update.Parameters.AddWithValue("@u", target.UserId);
            update.Parameters.AddWithValue("@id", target.CharacterId);
            if (await update.ExecuteNonQueryAsync() != 1)
            {
                await transaction.RollbackAsync();
                return new CharacterDeleteOutcome(CharacterDeleteResult.NotFound);
            }

            await transaction.CommitAsync();
            return new CharacterDeleteOutcome(
                CharacterDeleteResult.DeleteKeySent, target.Row.AccountName, target.Row.Name, email, key);
        }

        private async Task<string> FindCharacterDeleteEmailAsync(string accountName)
        {
            await using var connection = new MySqlConnection(_userConn);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT e_mail FROM user WHERE id=@account AND e_mail LIKE '%@%' LIMIT 1", connection);
            command.Parameters.AddWithValue("@account", accountName);
            return Convert.ToString(await command.ExecuteScalarAsync())?.Trim() ?? "";
        }

        private static string CreateCharacterDeleteKey()
        {
            const string alphabet = "0123456789ABab";
            Span<char> key = stackalloc char[10];
            for (int i = 0; i < key.Length; i++)
                key[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            return new string(key);
        }

        private static async Task HardDeleteCharacterAsync(
            MySqlConnection connection, MySqlTransaction transaction, CharacterDeleteTarget target)
        {
            foreach (string sql in new[]
            {
                "DELETE FROM useriteminfo WHERE characterid=@id",
                "DELETE FROM userstageinfo WHERE characterid=@id",
                "DELETE FROM characterinfo WHERE userid=@u AND id=@id"
            })
            {
                await using var command = new MySqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@u", target.UserId);
                command.Parameters.AddWithValue("@id", target.CharacterId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task SoftDeleteCharacterAsync(
            MySqlConnection connection, MySqlTransaction transaction, CharacterDeleteTarget target)
        {
            await using var command = new MySqlCommand(
                "UPDATE characterinfo SET auth=10,changetime=NOW() WHERE userid=@u AND id=@id",
                connection, transaction);
            command.Parameters.AddWithValue("@u", target.UserId);
            command.Parameters.AddWithValue("@id", target.CharacterId);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("personagem desapareceu durante soft-delete");
        }

        private static async Task InsertCharacterDeleteLogAsync(
            MySqlConnection connection, MySqlTransaction transaction,
            CharacterDeleteTarget target, byte mode)
        {
            await using var command = new MySqlCommand(
                "INSERT INTO logdeletecharacter(userid,charname,deletetime,level,mode) " +
                "VALUES(@u,@name,NOW(6),@level,@mode)", connection, transaction);
            command.Parameters.AddWithValue("@u", target.UserId);
            command.Parameters.AddWithValue("@name", target.Row.Name);
            command.Parameters.AddWithValue("@level", target.Row.Level);
            command.Parameters.AddWithValue("@mode", mode);
            await command.ExecuteNonQueryAsync();
        }

        private sealed record CharacterDeleteRow(
            string Name, byte Level, bool Used, int AgeDays, string ActiveName,
            string AccountName, string StoredKey, bool KeyIsRecent);

        private sealed record CharacterDeleteTarget(
            int UserId, int CharacterId, CharacterDeleteRow Row);
    }
}
