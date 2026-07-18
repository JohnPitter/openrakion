using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    /// <summary>Repositorio de itens: useriteminfo, itembox (box/refino), quickslot, defs e cash/gold.</summary>
    public sealed partial class WorldDatabase
    {
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
        public async Task<System.Collections.Generic.List<(int Id, int ItemId, int Level)>> LoadItemBoxAsync(int userId)
        {
            var list = new System.Collections.Generic.List<(int, int, int)>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT id,itemid,level FROM itembox WHERE userid=@uid AND qslot=0 ORDER BY id", c);
                cmd.Parameters.AddWithValue("@uid", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
            }
            catch (Exception ex) { Log.Error("db", "LoadItemBoxAsync({0}): {1}", userId, ex.Message); }
            return list;
        }

        /// <summary>Grava o nível de refino (enchant) de UMA linha do armazém (itembox) pelo id exato.
        /// Usado no commit do refino p/ persistir o +N da arma.</summary>
        public async Task UpdateItemBoxLevelAsync(int rowId, int level)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("UPDATE itembox SET level=@lv WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@lv", (byte)System.Math.Clamp(level, 0, 15));
                cmd.Parameters.AddWithValue("@id", rowId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "UpdateItemBoxLevelAsync({0},{1}): {2}", rowId, level, ex.Message); }
        }

        /// <summary>Remove UMA linha do armazém (itembox) pelo id EXATO — consumo do refino (catalyzer/materiais).
        /// Preciso (não fungível por itemId): some só a célula refinada.</summary>
        public async Task DeleteItemBoxByIdAsync(int rowId)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("DELETE FROM itembox WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@id", rowId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "DeleteItemBoxByIdAsync({0}): {1}", rowId, ex.Message); }
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
    }
}
