using MySqlConnector;

namespace RakionServer.Admin;

/// <summary>
/// Acesso ao DB `rakion` para o painel admin (operações CRUD distintas do runtime do jogo).
/// Conta = `user` (login) + `usergameinfo` (perfil) + `cash` (saldo cash, id = nome da conta).
/// </summary>
public sealed class AdminDb(IConfiguration cfg)
{
    private readonly string _conn = cfg.GetConnectionString("Rakion")
        ?? "Server=127.0.0.1;Port=3306;Database=rakion;Uid=root;Pwd=123456;";

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        return c;
    }

    // ---- contas ----

    public async Task<List<AccountRow>> ListAccountsAsync(string? search, int limit = 100)
    {
        var list = new List<AccountRow>();
        await using var c = await OpenAsync();
        var sql = "SELECT u.id, IFNULL(g.id,0), u.Authority, IFNULL(g.charname,''), IFNULL(g.gold,0), " +
                  "IFNULL(ca.cash,0), (g.powertimedate > NOW()), IFNULL(g.ban,0) " +
                  "FROM user u LEFT JOIN usergameinfo g ON g.name=u.id LEFT JOIN cash ca ON ca.id=u.id " +
                  (string.IsNullOrWhiteSpace(search) ? "" : "WHERE u.id LIKE @s ") +
                  "ORDER BY u.id LIMIT @lim";
        await using var cmd = new MySqlCommand(sql, c);
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@s", "%" + search + "%");
        cmd.Parameters.AddWithValue("@lim", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new AccountRow(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3),
                r.GetInt64(4), r.GetInt64(5), !r.IsDBNull(6) && r.GetBoolean(6), r.GetInt32(7) != 0));
        return list;
    }

    public async Task<AccountRow?> GetAccountAsync(string id)
        => (await ListAccountsAsync(id, 1)).Find(a => a.Id == id);

    public async Task<bool> CreateAccountAsync(string id, string password, int country = 1)
    {
        await using var c = await OpenAsync();
        await using (var u = new MySqlCommand("INSERT INTO user (id, password, Authority, country) VALUES (@id,@pw,0,@co)", c))
        {
            u.Parameters.AddWithValue("@id", id);
            u.Parameters.AddWithValue("@pw", password);
            u.Parameters.AddWithValue("@co", country);
            await u.ExecuteNonQueryAsync();
        }
        await using (var g = new MySqlCommand(
            "INSERT INTO usergameinfo (name, createtime, lastconnect, country, tutorial) VALUES (@id, NOW(), NOW(), @co, 1)", c))
        {
            g.Parameters.AddWithValue("@id", id);
            g.Parameters.AddWithValue("@co", country);
            await g.ExecuteNonQueryAsync();
        }
        return true;
    }

    public async Task SetPasswordAsync(string id, string password)
        => await NonQuery("UPDATE user SET password=@pw WHERE id=@id", ("@pw", password), ("@id", id));

    public async Task SetBanAsync(string id, bool ban)
        => await NonQuery("UPDATE usergameinfo SET ban=@b WHERE name=@id", ("@b", ban ? 1 : 0), ("@id", id));

    // ---- gold / cash ----

    public async Task SetGoldAsync(int giId, long value)
        => await NonQuery("UPDATE usergameinfo SET gold=@v WHERE id=@id", ("@v", value), ("@id", giId));

    public async Task SetCashAsync(string accountName, long value)
        => await NonQuery("INSERT INTO cash (id, cash) VALUES (@id,@v) ON DUPLICATE KEY UPDATE cash=@v",
            ("@id", accountName), ("@v", value));

    // ---- Power User ----

    public async Task SetPowerUserAsync(int giId, int bonusPoints, int days)
        => await NonQuery(
            "UPDATE usergameinfo SET powerlevelpoint=@b, powertime=GREATEST(powertime,1), " +
            "powertimedate=DATE_ADD(NOW(), INTERVAL @d DAY) WHERE id=@id",
            ("@b", bonusPoints), ("@d", days), ("@id", giId));

    public async Task ClearPowerUserAsync(int giId)
        => await NonQuery("UPDATE usergameinfo SET powerlevelpoint=0, powertime=0, powertimedate=NOW() WHERE id=@id",
            ("@id", giId));

    // ---- personagens / itens ----

    public async Task<List<CharRow>> ListCharsAsync(int giId)
    {
        var list = new List<CharRow>();
        await using var c = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, name, Class, level, used, levelpoint FROM characterinfo WHERE userid=@id ORDER BY slot", c);
        cmd.Parameters.AddWithValue("@id", giId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new CharRow(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4) != 0, r.GetInt32(5)));
        return list;
    }

    public async Task<CharFull?> GetActiveCharAsync(int giId)
    {
        await using var c = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id,name,Class,level,levelpoint,hit1,hit2,hit3,hit4,chit,hp,ap,attackspeed,speed,maxcp " +
            "FROM characterinfo WHERE userid=@id ORDER BY used DESC, slot LIMIT 1", c);
        cmd.Parameters.AddWithValue("@id", giId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var stats = new int[10];
        for (int i = 0; i < 10; i++) stats[i] = r.GetInt32(5 + i);
        return new CharFull(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), stats);
    }

    public async Task<List<EquipSlot>> ListEquipAsync(int charId)
    {
        var list = new List<EquipSlot>();
        await using var c = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT ui.slot, ui.itemid, IFNULL(ii.type,-1) FROM useriteminfo ui " +
            "LEFT JOIN iteminfo ii ON ii.id=ui.itemid WHERE ui.characterid=@c ORDER BY ui.slot", c);
        cmd.Parameters.AddWithValue("@c", charId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new EquipSlot(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
        return list;
    }

    public async Task<List<BoxItemRow>> ListBoxAsync(int giId)
    {
        var list = new List<BoxItemRow>();
        await using var c = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT ib.id, ib.itemid, ib.qslot, IFNULL(ii.type,-1) FROM itembox ib " +
            "LEFT JOIN iteminfo ii ON ii.id=ib.itemid WHERE ib.userid=@id ORDER BY ib.id", c);
        cmd.Parameters.AddWithValue("@id", giId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new BoxItemRow(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
        return list;
    }

    public async Task AddItemBoxAsync(int giId, int itemId)
        => await NonQuery("INSERT INTO itembox (userid, itemid, limittime, qslot) VALUES (@u,@i,0,0)",
            ("@u", giId), ("@i", itemId));

    public async Task DeleteBoxItemAsync(int itemboxId)
        => await NonQuery("DELETE FROM itembox WHERE id=@id", ("@id", itemboxId));

    // iteminfo NÃO tem nome (os nomes vivem no items.dat do cliente). Busca por id (substring) e/ou type.
    public async Task<List<ItemDef>> SearchItemsAsync(string? term, int? type, int limit = 60)
    {
        var list = new List<ItemDef>();
        await using var c = await OpenAsync();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(term)) where.Add("CAST(id AS CHAR) LIKE @t");
        if (type is not null) where.Add("type=@ty");
        var sql = "SELECT id, type FROM iteminfo " +
                  (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) + " " : "") +
                  "ORDER BY id LIMIT @lim";
        await using var cmd = new MySqlCommand(sql, c);
        if (!string.IsNullOrWhiteSpace(term)) cmd.Parameters.AddWithValue("@t", "%" + term + "%");
        if (type is not null) cmd.Parameters.AddWithValue("@ty", type);
        cmd.Parameters.AddWithValue("@lim", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new ItemDef(r.GetInt32(0), r.GetInt32(1)));
        return list;
    }

    // ---- pu_config ----

    public async Task<PuConfigForm> LoadPuConfigAsync()
    {
        var f = new PuConfigForm();
        await using var c = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT price,bonus_points,duration_days,exp_mult,gold_mult,promo_active," +
            "promo_exp_mult,promo_gold_mult,promo_start,promo_end FROM pu_config WHERE id=1", c);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            f.Price = r.GetInt32(0); f.BonusPoints = r.GetInt32(1); f.DurationDays = r.GetInt32(2);
            f.ExpMult = r.GetDecimal(3); f.GoldMult = r.GetDecimal(4); f.PromoActive = r.GetInt32(5) != 0;
            f.PromoExpMult = r.GetDecimal(6); f.PromoGoldMult = r.GetDecimal(7);
            f.PromoStart = r.IsDBNull(8) ? null : r.GetDateTime(8);
            f.PromoEnd = r.IsDBNull(9) ? null : r.GetDateTime(9);
        }
        return f;
    }

    public async Task SavePuConfigAsync(PuConfigForm f)
        => await NonQuery(
            "UPDATE pu_config SET price=@p, bonus_points=@b, duration_days=@d, exp_mult=@e, gold_mult=@g, " +
            "promo_active=@pa, promo_exp_mult=@pe, promo_gold_mult=@pg, promo_start=@ps, promo_end=@pen WHERE id=1",
            ("@p", f.Price), ("@b", f.BonusPoints), ("@d", f.DurationDays), ("@e", f.ExpMult), ("@g", f.GoldMult),
            ("@pa", f.PromoActive ? 1 : 0), ("@pe", f.PromoExpMult), ("@pg", f.PromoGoldMult),
            ("@ps", (object?)f.PromoStart ?? DBNull.Value), ("@pen", (object?)f.PromoEnd ?? DBNull.Value));

    // ---- helper ----

    private async Task NonQuery(string sql, params (string, object)[] ps)
    {
        await using var c = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, c);
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync();
    }
}
