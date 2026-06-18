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
    public sealed class WorldDatabase
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
                Log.Ok("db", "schema verificado (itembox.qslot, pu_config)");
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
                    "FROM useriteminfo WHERE characterid=@c", c);
                cmd.Parameters.AddWithValue("@c", characterId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    list.Add(new UserItem
                    {
                        Id = r.GetInt32(0), UserId = r.GetInt32(1), CharacterId = r.GetInt32(2),
                        ItemId = r.GetInt32(3), ItemSn = r.GetInt32(4), SnType = (byte)r.GetInt32(5),
                        Level = (byte)r.GetInt32(6), LimitTime = r.GetInt32(7), Slot = (byte)r.GetInt32(8),
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

        /// <summary>Insere um item comprado no ARMAZEM (itembox), nao no useriteminfo (que e' a aparencia
        /// equipada e renderiza no corpo -> crash). userId = usergameinfo.id (conta). Retorna o id (0=falha).</summary>
        public async Task<int> InsertItemBoxAsync(int userId, int itemId, int limitTime = 0)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "INSERT INTO itembox (userid,itemid,limittime) VALUES (@uid,@iid,@lt); SELECT LAST_INSERT_ID();", c);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@iid", itemId);
                cmd.Parameters.AddWithValue("@lt", limitTime);
                object? v = await cmd.ExecuteScalarAsync();
                return v == null ? 0 : Convert.ToInt32(v);
            }
            catch (Exception ex) { Log.Error("db", "InsertItemBoxAsync({0},{1}): {2}", userId, itemId, ex.Message); return 0; }
        }

        /// <summary>Desempacota UM set (type 10) do ARMAZEM: numa transação, remove a linha do set (qslot=0) e
        /// insere as peças membros com o MESMO limittime. Atômico (rollback em falha); só insere se o set
        /// existia -> idempotente, sem duplicar. Retorna o nº de peças inseridas (0 = set ausente/já feito).</summary>
        public async Task<int> UnpackSetInBoxAsync(int userId, int setItemId, IReadOnlyList<int> members)
        {
            if (members.Count == 0) return 0;
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var tx = await c.BeginTransactionAsync();
                int rowId, limit;
                await using (var sel = new MySqlCommand(
                    "SELECT id,limittime FROM itembox WHERE userid=@uid AND itemid=@iid AND qslot=0 ORDER BY id LIMIT 1", c, tx))
                {
                    sel.Parameters.AddWithValue("@uid", userId);
                    sel.Parameters.AddWithValue("@iid", setItemId);
                    await using var r = await sel.ExecuteReaderAsync();
                    if (!await r.ReadAsync()) return 0;            // set ausente -> tx (await using) faz rollback
                    rowId = r.GetInt32(0); limit = r.GetInt32(1);
                }
                await using (var del = new MySqlCommand("DELETE FROM itembox WHERE id=@id", c, tx))
                {
                    del.Parameters.AddWithValue("@id", rowId);
                    await del.ExecuteNonQueryAsync();
                }
                foreach (var m in members)
                {
                    await using var ins = new MySqlCommand(
                        "INSERT INTO itembox (userid,itemid,limittime,qslot) VALUES (@uid,@iid,@lt,0)", c, tx);
                    ins.Parameters.AddWithValue("@uid", userId);
                    ins.Parameters.AddWithValue("@iid", m);
                    ins.Parameters.AddWithValue("@lt", limit);
                    await ins.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                Log.Ok("shop", "set {0} desempacotado (user {1}): {2} peças, limittime {3}", setItemId, userId, members.Count, limit);
                return members.Count;
            }
            catch (Exception ex) { Log.Error("db", "UnpackSetInBoxAsync({0},{1}): {2}", userId, setItemId, ex.Message); return 0; }
        }

        /// <summary>Remove UMA linha do ARMAZEM (itembox, qslot=0) com o itemId dado — a VENDA de um item do
        /// box. Itens sao fungiveis por itemId, entao apaga a 1a linha (ORDER BY id) com esse id.</summary>
        public async Task DeleteItemBoxByItemAsync(int userId, int itemId)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "DELETE FROM itembox WHERE userid=@uid AND itemid=@iid AND qslot=0 ORDER BY id LIMIT 1", c);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@iid", itemId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "DeleteItemBoxByItemAsync({0},{1}): {2}", userId, itemId, ex.Message); }
        }

        /// <summary>Carrega os itens do BOX (itembox, qslot=0) de uma conta, em ordem (= ordem dos slots do box).
        /// Itens com qslot>0 estao no quickslot de pocao (ver LoadQuickslotAsync) e nao aparecem no box.</summary>
        public async Task<System.Collections.Generic.List<int>> LoadItemBoxAsync(int userId)
        {
            var list = new System.Collections.Generic.List<int>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT itemid FROM itembox WHERE userid=@uid AND qslot=0 ORDER BY id", c);
                cmd.Parameters.AddWithValue("@uid", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add(r.GetInt32(0));
            }
            catch (Exception ex) { Log.Error("db", "LoadItemBoxAsync({0}): {1}", userId, ex.Message); }
            return list;
        }

        /// <summary>Carrega o quickslot consolidado por id: (celula = menor qslot-1, itemId, quantidade do stack).</summary>
        public async Task<System.Collections.Generic.List<(int Cell, int ItemId, int Count)>> LoadQuickslotAsync(int userId)
        {
            var list = new System.Collections.Generic.List<(int, int, int)>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT MIN(qslot) AS qslot, itemid, COUNT(*) AS cnt FROM itembox WHERE userid=@uid AND qslot>0 GROUP BY itemid", c);
                cmd.Parameters.AddWithValue("@uid", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add((System.Convert.ToInt32(r.GetValue(0)) - 1, r.GetInt32(1), System.Convert.ToInt32(r.GetValue(2))));
            }
            catch (Exception ex) { Log.Error("db", "LoadQuickslotAsync({0}): {1}", userId, ex.Message); }
            return list;
        }

        /// <summary>Persiste o quickslot de pocao reconciliando o itembox pelo itemId (pocoes do mesmo id sao
        /// fungiveis): zera o qslot da conta e remarca uma linha do box por celula ocupada. O estado vem do
        /// modelo da sessao (_potionSlot) apos o swap 0x31, entao sempre ha uma linha qslot=0 correspondente.</summary>
        public async Task SaveQuickslotAsync(int userId, System.Collections.Generic.IReadOnlyList<int> potionSlot)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using (var reset = new MySqlCommand("UPDATE itembox SET qslot=0 WHERE userid=@uid", c))
                {
                    reset.Parameters.AddWithValue("@uid", userId);
                    await reset.ExecuteNonQueryAsync();
                }
                for (int cell = 0; cell < potionSlot.Count; cell++)
                {
                    int item = potionSlot[cell];
                    if (item == 0) continue;
                    await using var mark = new MySqlCommand(
                        "UPDATE itembox SET qslot=@q WHERE userid=@uid AND itemid=@it AND qslot=0", c);   // TODAS as linhas do id (stack) na celula
                    mark.Parameters.AddWithValue("@q", cell + 1);
                    mark.Parameters.AddWithValue("@uid", userId);
                    mark.Parameters.AddWithValue("@it", item);
                    await mark.ExecuteNonQueryAsync();
                }
                Log.Ok("shop", "quickslot persistido (uid={0})", userId);
            }
            catch (Exception ex) { Log.Error("db", "SaveQuickslotAsync({0}): {1}", userId, ex.Message); }
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
                        Id = r.GetInt32(0), Type = (byte)r.GetInt32(1), Class = (byte)r.GetInt32(2),
                        Level = (byte)r.GetInt32(3), Shop = (byte)r.GetInt32(4), Gold = r.GetInt32(5),
                        Cash = r.GetInt32(6), Hit1 = r.GetInt32(7), Hit2 = r.GetInt32(8), Hit3 = r.GetInt32(9),
                        Hit4 = r.GetInt32(10), CHit = r.GetInt32(11), Ap = r.GetInt32(12), Hp = r.GetInt32(13),
                        MaxCp = r.GetInt32(14), Power = r.GetInt32(15),
                    });
            }
            catch (Exception ex) { Log.Error("db", "LoadItemDefsAsync: {0}", ex.Message); }
            return list;
        }

        /// <summary>Ajusta o cash de uma CONTA (tabela `cash`, id=char(16)=nome da conta). delta pode ser negativo.</summary>
        public async Task AddCashAsync(string accountId, int delta)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "INSERT INTO cash (id, cash) VALUES (@id, GREATEST(0,@d)) " +
                    "ON DUPLICATE KEY UPDATE cash=GREATEST(0, cash+@d)", c);
                cmd.Parameters.AddWithValue("@id", accountId);
                cmd.Parameters.AddWithValue("@d", delta);
                await cmd.ExecuteNonQueryAsync();
                Log.Debug("db", "AddCash: acct='{0}' delta={1}", accountId, delta);
            }
            catch (Exception ex) { Log.Error("db", "AddCashAsync('{0}',{1}): {2}", accountId, delta, ex.Message); }
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

        /// <summary>Persiste o MELHOR rank do char num stage (userstageinfo). rank = grade do 0x53 (cfgA):
        /// 0=nenhum, 1=D, 2=C, 3=B, 4=A, 5=S (maior = melhor). UPDATE-then-INSERT (sem depender de unique key).</summary>
        public async Task SaveStageRankAsync(int characterId, byte stage, int rank)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var up = new MySqlCommand(
                    "UPDATE userstageinfo SET `rank`=GREATEST(`rank`,@r), updatetime=NOW() WHERE characterid=@cid AND stage=@st", c);
                up.Parameters.AddWithValue("@r", rank);
                up.Parameters.AddWithValue("@cid", characterId);
                up.Parameters.AddWithValue("@st", stage);
                if (await up.ExecuteNonQueryAsync() == 0)
                {
                    await using var ins = new MySqlCommand(
                        "INSERT INTO userstageinfo (characterid,stage,`rank`,updatetime) VALUES (@cid,@st,@r,NOW())", c);
                    ins.Parameters.AddWithValue("@cid", characterId);
                    ins.Parameters.AddWithValue("@st", stage);
                    ins.Parameters.AddWithValue("@r", rank);
                    await ins.ExecuteNonQueryAsync();
                }
                Log.Ok("db", "userstageinfo: char {0} stage {1} rank {2} (melhor)", characterId, stage, rank);
            }
            catch (Exception ex) { Log.Error("db", "SaveStageRankAsync({0},{1}): {2}", characterId, stage, ex.Message); }
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

        /// <summary>Persiste a alocacao de 1 ponto de stat (0x33): incrementa a coluna do stat e decrementa
        /// levelpoint, atomico e so' enquanto ha' levelpoint. statIdx 0..9 -> hit1..maxcp (ordem da tela de
        /// status). Retorna o n de linhas afetadas (0 = sem level-point). col vem de array fixo (sem injecao).</summary>
        public async Task<int> AllocateStatAsync(int characterId, int statIdx)
        {
            string[] cols = { "hit1", "hit2", "hit3", "hit4", "chit", "hp", "ap", "attackspeed", "speed", "maxcp" };
            if (statIdx < 0 || statIdx >= cols.Length) return 0;
            string col = cols[statIdx];
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    $"UPDATE characterinfo SET {col}={col}+1, levelpoint=levelpoint-1 WHERE id=@id AND levelpoint>0", c);
                cmd.Parameters.AddWithValue("@id", characterId);
                int n = await cmd.ExecuteNonQueryAsync();
                Log.Ok("db", "AllocateStat: char={0} stat={1} ({2}) linhas={3}", characterId, statIdx, col, n);
                return n;
            }
            catch (Exception ex) { Log.Error("db", "AllocateStatAsync({0},{1}): {2}", characterId, statIdx, ex.Message); return 0; }
        }

        /// <summary>Persiste a alocacao de 1 ponto de PU BONUS (FUN_0040b3d0, quando levelpoint==0):
        /// incrementa a coluna do stat (characterinfo) e decrementa usergameinfo.powerlevelpoint.
        /// col vem de array fixo (sem injecao).</summary>
        public async Task AllocateStatPuAsync(int characterId, int gameInfoId, int statIdx)
        {
            string[] cols = { "hit1", "hit2", "hit3", "hit4", "chit", "hp", "ap", "attackspeed", "speed", "maxcp" };
            if (statIdx < 0 || statIdx >= cols.Length) return;
            string col = cols[statIdx];
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using (var cmd1 = new MySqlCommand($"UPDATE characterinfo SET {col}={col}+1 WHERE id=@cid", c))
                {
                    cmd1.Parameters.AddWithValue("@cid", characterId);
                    await cmd1.ExecuteNonQueryAsync();
                }
                await using (var cmd2 = new MySqlCommand(
                    "UPDATE usergameinfo SET powerlevelpoint=powerlevelpoint-1 WHERE id=@gid AND powerlevelpoint>0", c))
                {
                    cmd2.Parameters.AddWithValue("@gid", gameInfoId);
                    await cmd2.ExecuteNonQueryAsync();
                }
                Log.Ok("db", "AllocateStatPu: char={0} stat={1} ({2}) gi={3}", characterId, statIdx, col, gameInfoId);
            }
            catch (Exception ex) { Log.Error("db", "AllocateStatPuAsync({0},{1}): {2}", characterId, statIdx, ex.Message); }
        }

        /// <summary>Concede Power User (compra do 0x34): soma bonusPoints ao powerlevelpoint e ESTENDE a
        /// validade por durationDays (a partir de hoje OU do vencimento atual, o que for maior — assim
        /// comprar de novo acumula). O original validava no cash-shop online (offline); aqui o servidor
        /// pessoal concede direto. durationDays/bonusPoints vêm da pu_config.</summary>
        public async Task GrantPowerUserAsync(int gameInfoId, int bonusPoints, int durationDays)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "UPDATE usergameinfo SET powerlevelpoint=powerlevelpoint+@b, powertime=GREATEST(powertime,1), " +
                    "powertimedate=DATE_ADD(GREATEST(NOW(), IFNULL(powertimedate, NOW())), INTERVAL @d DAY) " +
                    "WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@b", bonusPoints);
                cmd.Parameters.AddWithValue("@d", durationDays);
                cmd.Parameters.AddWithValue("@id", gameInfoId);
                await cmd.ExecuteNonQueryAsync();
                Log.Ok("db", "GrantPowerUser: gi={0} +{1} bonus points, +{2}d", gameInfoId, bonusPoints, durationDays);
            }
            catch (Exception ex) { Log.Error("db", "GrantPowerUserAsync({0}): {1}", gameInfoId, ex.Message); }
        }

        // Colunas do char p/ o char-select (0x0C) — UMA fonte (ordem = índices em MapCharacter).
        private const string CharSelectColumns =
            "id,userid,name,used,Class,level,win,lose,draw,exp,levelpoint,slot," +
            "hit1,hit2,hit3,hit4,chit,hp,ap,attackspeed,speed,maxcp,totalrank,classrank";

        private static CharacterInfo MapCharacter(System.Data.Common.DbDataReader r) => new CharacterInfo
        {
            Id = r.GetInt32(0), UserId = r.GetInt32(1), Name = r.GetString(2),
            Used = r.GetInt32(3) != 0, Class = (byte)r.GetInt32(4), Level = (byte)r.GetInt32(5),
            Win = r.GetInt32(6), Lose = r.GetInt32(7), Draw = r.GetInt32(8), Exp = r.GetInt32(9),
            LevelPoint = (byte)r.GetInt32(10), Slot = (byte)r.GetInt32(11),
            Hit1 = (byte)r.GetInt32(12), Hit2 = (byte)r.GetInt32(13), Hit3 = (byte)r.GetInt32(14),
            Hit4 = (byte)r.GetInt32(15), Chit = (byte)r.GetInt32(16), Hp = (byte)r.GetInt32(17),
            Ap = (byte)r.GetInt32(18), AttackSpeed = (byte)r.GetInt32(19), Speed = (byte)r.GetInt32(20),
            Maxcp = (byte)r.GetInt32(21), TotalRank = r.GetInt32(22), ClassRank = r.GetInt32(23),
        };

        /// <summary>Char ativo da conta (used=1; tiebreak slot). null se nao tem char.</summary>
        public async Task<CharacterInfo?> LoadActiveCharacterAsync(int userId)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT " + CharSelectColumns + " FROM characterinfo WHERE userid=@u ORDER BY used DESC, slot ASC LIMIT 1", c);
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
                    "SELECT " + CharSelectColumns + " FROM characterinfo WHERE userid=@u AND used<>0 ORDER BY slot ASC", c);
                cmd.Parameters.AddWithValue("@u", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add(MapCharacter(r));
            }
            catch (Exception ex) { Log.Error("db", "LoadCharactersAsync({0}): {1}", userId, ex.Message); }
            return list;
        }
    }
}
