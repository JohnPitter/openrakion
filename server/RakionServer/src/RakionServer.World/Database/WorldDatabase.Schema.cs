using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    /// <summary>
    /// Provisionamento de schema do <see cref="WorldDatabase"/> (fatiado por tamanho). Idempotente
    /// (IF NOT EXISTS), roda no boot p/ sobreviver a um re-import do dump v258: provisiona o que o dump não tem
    /// mas o servidor offline usa — itembox.qslot/level, pu_config, enchant_*, e o domínio messenger (buddylist,
    /// messenger_session, usergameinfo.buddyname).
    /// </summary>
    public sealed partial class WorldDatabase
    {
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
                // Messenger/amigos (botão F9): amizade persistida (buddylist do dump v258 — recriada se faltar,
                // com PK p/ idempotência), identidade do buddy por IP (messenger_session, gravada pelo World no
                // login e apagada no logout — o login do messenger é cifrado, então a identidade vem do World) e o
                // nick do messenger (usergameinfo.buddyname, sincronizado pelo nick change 0x15).
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS buddylist (" +
                    " Id VARCHAR(16) NOT NULL," +
                    " Buddy VARCHAR(16) NOT NULL," +
                    " Category VARCHAR(20) NOT NULL DEFAULT ''," +
                    " PRIMARY KEY (Id, Buddy))");
                await Exec(c,
                    "CREATE TABLE IF NOT EXISTS messenger_session (" +
                    " account VARCHAR(16) NOT NULL PRIMARY KEY," +
                    " char_name VARCHAR(16) NOT NULL DEFAULT ''," +
                    " ip VARCHAR(45) NOT NULL DEFAULT ''," +
                    " port INT NOT NULL DEFAULT 0," +
                    " login_ts DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP)");
                // a tabela pode existir de versões anteriores sem char_name/port -> adiciona idempotente. port =
                // porta TCP de origem do login no World; o Buddy desambigua 2+ clientes do mesmo IP por
                // proximidade de porta (a conexão do Buddy nasce logo após o login, efêmeras vizinhas).
                await Exec(c, "ALTER TABLE messenger_session ADD COLUMN IF NOT EXISTS char_name VARCHAR(16) NOT NULL DEFAULT ''");
                await Exec(c, "ALTER TABLE messenger_session ADD COLUMN IF NOT EXISTS port INT NOT NULL DEFAULT 0");
                await Exec(c, "ALTER TABLE usergameinfo ADD COLUMN IF NOT EXISTS buddyname VARCHAR(16) NOT NULL DEFAULT ''");
                Log.Ok("db", "schema verificado (itembox.qslot, pu_config, enchant_*, buddylist, messenger_session)");
            }
            catch (Exception ex) { Log.Error("db", "EnsureSchemaAsync: {0}", ex.Message); }
        }

        private static async Task Exec(MySqlConnection c, string sql)
        {
            await using var cmd = new MySqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
