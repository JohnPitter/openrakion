using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    /// <summary>Provisionamento de schema (EnsureSchema) e cargas de config (pu_config, enchant_*).</summary>
    public sealed partial class WorldDatabase
    {
        /// <summary>
        /// Provisiona o que o dump v258 nao tem mas o servidor offline usa. Idempotente (IF NOT EXISTS)
        /// — roda no boot p/ sobreviver a um re-import do dump. `itembox.qslot` marca a posicao de um
        /// consumivel (0 = box, N = celula N-1 do quickslot); `pu_config` guarda preco/bonus/multiplicadores
        /// do Power User (linha unica id=1, editavel pelo painel admin).
        /// </summary>
        public async Task EnsureSchemaAsync()
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await Exec(c, "ALTER TABLE itembox ADD COLUMN IF NOT EXISTS qslot TINYINT NOT NULL DEFAULT 0");
                await Exec(c, "ALTER TABLE itembox ADD COLUMN IF NOT EXISTS level TINYINT NOT NULL DEFAULT 0");
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS pu_config (" +
                    " id TINYINT NOT NULL PRIMARY KEY," +
                    " price INT NOT NULL DEFAULT 8000," +
                    " bonus_points SMALLINT NOT NULL DEFAULT 51," +
                    " duration_days SMALLINT NOT NULL DEFAULT 30," +
                    " exp_mult DECIMAL(4,2) NOT NULL DEFAULT 1.50," +
                    " gold_mult DECIMAL(4,2) NOT NULL DEFAULT 1.50," +
                    " promo_active TINYINT(1) NOT NULL DEFAULT 0," +
                    " promo_exp_mult DECIMAL(4,2) NOT NULL DEFAULT 2.00," +
                    " promo_gold_mult DECIMAL(4,2) NOT NULL DEFAULT 2.00," +
                    " promo_start DATETIME NULL," +
                    " promo_end DATETIME NULL)");
                await Exec(c, "INSERT IGNORE INTO pu_config (id) VALUES (1)");
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
                    " (13001,'Mithril',0.950,0.100,4)," +
                    " (13002,'Adamantium',0.920,0.080,14)," +
                    " (13003,'Orehalcon',0.900,0.070,14)," +
                    " (13004,'test+1',0.850,0.050,9)," +
                    " (13005,'test+2',0.800,0.040,14)");
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
                // Sessao do messenger: o buddy (processo separado; login cifrado AES-ECB ilegivel) descobre
                // a identidade de cada conexao por aqui — o world grava (account, ip) no login. Limpa no boot
                // (sessoes stale de um crash anterior). Ver BuddyDatabase.ResolveAccountByIpAsync.
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS messenger_session (" +
                    " account VARCHAR(16) NOT NULL PRIMARY KEY," +
                    " ip VARCHAR(45) NOT NULL DEFAULT ''," +
                    " login_ts DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP)");
                await Exec(c, "DELETE FROM messenger_session");
                // Auditoria do OpenGuard (Security/DbViolationSink escreve; painel admin le).
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS anticheat_log (" +
                    " id INT NOT NULL AUTO_INCREMENT PRIMARY KEY," +
                    " ts DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP," +
                    " slot SMALLINT UNSIGNED NOT NULL," +
                    " account VARCHAR(32) NOT NULL DEFAULT ''," +
                    " kind VARCHAR(24) NOT NULL," +
                    " severity TINYINT NOT NULL," +
                    " score INT NOT NULL," +
                    " action VARCHAR(8) NOT NULL," +
                    " hits INT NOT NULL DEFAULT 1," +
                    " detail VARCHAR(128) NOT NULL DEFAULT ''," +
                    " INDEX idx_ts (ts), INDEX idx_account (account))");
                Log.Ok("db", "schema verificado (itembox.qslot, pu_config, enchant_*, messenger_session, anticheat_log)");
            }
            catch (Exception ex) { Log.Error("db", "EnsureSchemaAsync: {0}", ex.Message); }
        }

        private static async Task Exec(MySqlConnection c, string sql)
        {
            await using var cmd = new MySqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync();
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
                    "SELECT price,bonus_points,duration_days,exp_mult,gold_mult,promo_active," +
                    "promo_exp_mult,promo_gold_mult,promo_start,promo_end FROM pu_config WHERE id=1", c);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    cfg.Price = r.GetInt32(0);
                    cfg.BonusPoints = r.GetInt32(1);
                    cfg.DurationDays = r.GetInt32(2);
                    cfg.ExpMult = (double)r.GetDecimal(3);
                    cfg.GoldMult = (double)r.GetDecimal(4);
                    cfg.PromoActive = r.GetInt32(5) != 0;
                    cfg.PromoExpMult = (double)r.GetDecimal(6);
                    cfg.PromoGoldMult = (double)r.GetDecimal(7);
                    cfg.PromoStart = r.IsDBNull(8) ? null : r.GetDateTime(8);
                    cfg.PromoEnd = r.IsDBNull(9) ? null : r.GetDateTime(9);
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
                    "SELECT jewel_floor,jewel_bonus,event_mult,pu_mult,floor_min,ceil_max,downgrade_lo,downgrade_hi" +
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
                    }
            }
            catch (Exception ex) { Log.Error("db", "LoadEnchantConfigAsync: {0}", ex.Message); }
            return cfg;
        }
    }
}
