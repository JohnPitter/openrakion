using MySqlConnector;

namespace RakionServer.Accounts;

/// <summary>Resultado da criação de conta. Created = ok; os demais = motivo da recusa (sem criar nada).</summary>
public enum CreateAccountResult { Created, AlreadyExists, InvalidId, InvalidPassword }

/// <summary>
/// Criação de conta — golden source compartilhado pelo painel admin (<c>AdminDb</c>) e pelo
/// <c>/register</c> do launcher web. Uma conta é o login em <c>user</c> + o perfil em <c>usergameinfo</c>
/// (<c>name = id</c>). A senha é texto plano (esquema do jogo: o <c>/launcherlogin</c> compara em claro).
/// Regra de negócio (validação de id/senha) separada do I/O (a checagem de duplicata + os 2 INSERTs correm
/// numa transação: sem ela, um INSERT parcial deixava <c>user</c> sem <c>usergameinfo</c>).
/// </summary>
public static class AccountStore
{
    public const int IdMin = 3, IdMax = 16, PwMin = 3, PwMax = 16;

    /// <summary>Valida id/senha (regra pura, sem I/O): id alfanumérico ASCII de <see cref="IdMin"/>..<see cref="IdMax"/>,
    /// senha de <see cref="PwMin"/>..<see cref="PwMax"/>. Devolve o motivo da recusa, ou null se válido.</summary>
    public static CreateAccountResult? Validate(string id, string password)
    {
        if (id.Length < IdMin || id.Length > IdMax || !IsAsciiAlnum(id)) return CreateAccountResult.InvalidId;
        if (password.Length < PwMin || password.Length > PwMax) return CreateAccountResult.InvalidPassword;
        return null;
    }

    private static bool IsAsciiAlnum(string s)
    {
        foreach (char ch in s)
            if (!char.IsAsciiLetterOrDigit(ch)) return false;
        return true;
    }

    /// <summary>Cria a conta se id/senha forem válidos e o id ainda não existir. Não cria duplicata
    /// (devolve <see cref="CreateAccountResult.AlreadyExists"/>). Os dois INSERTs correm numa transação.</summary>
    public static async Task<CreateAccountResult> CreateAsync(string connString, string id, string password, int country = 1)
    {
        if (Validate(id, password) is { } invalid) return invalid;

        await using var c = new MySqlConnection(connString);
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();

        await using (var exists = new MySqlCommand("SELECT 1 FROM user WHERE id=@id", c, tx))
        {
            exists.Parameters.AddWithValue("@id", id);
            if (await exists.ExecuteScalarAsync() is not null) return CreateAccountResult.AlreadyExists;
        }
        await using (var u = new MySqlCommand(
            "INSERT INTO user (id, password, Authority, country) VALUES (@id,@pw,0,@co)", c, tx))
        {
            u.Parameters.AddWithValue("@id", id);
            u.Parameters.AddWithValue("@pw", password);
            u.Parameters.AddWithValue("@co", country);
            await u.ExecuteNonQueryAsync();
        }
        await using (var g = new MySqlCommand(
            "INSERT INTO usergameinfo (name, createtime, lastconnect, country, tutorial) VALUES (@id, NOW(), NOW(), @co, 1)", c, tx))
        {
            g.Parameters.AddWithValue("@id", id);
            g.Parameters.AddWithValue("@co", country);
            await g.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return CreateAccountResult.Created;
    }
}
