using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>Um amigo na lista (buddylist): conta dona do amigo, nick exibido e categoria/grupo.</summary>
    public readonly record struct BuddyEntry(string Account, string Nick, string Category);

    /// <summary>
    /// Acesso ao banco `rakion` para o Buddy. Resolve a identidade da conexão (o login do messenger é cifrado,
    /// então o Buddy não sabe quem conectou; o World grava <c>messenger_session(account, char_name, ip)</c> no
    /// login e o Buddy resolve por IP), carrega a lista de amigos (buddylist + nick) e remove amizade. A ESCRITA
    /// da amizade (add) mora no World (handler 0x19); aqui só leitura + remove. SQL sempre parametrizado.
    /// </summary>
    public sealed class BuddyDatabase
    {
        private readonly string _conn;
        public BuddyDatabase(string conn) => _conn = conn;

        /// <summary>Testa a conexão; loga o resultado. Buddy degrada de forma graciosa se o DB cair (lista vazia).</summary>
        public async Task<bool> PingAsync()
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT 1", c);
                await cmd.ExecuteScalarAsync();
                Log.Ok("buddy", "DB conectado");
                return true;
            }
            catch (Exception ex) { Log.Error("buddy", "DB falhou: {0}", ex.Message); return false; }
        }

        /// <summary>Todas as identidades (account, nick) de um IP — o World grava uma messenger_session por conta
        /// logada. Ordenadas da MAIS RECENTE p/ a mais antiga. Vazio = sem sessão de world nesse IP (cliente não
        /// logado / DB indisponível). Devolver a LISTA (não só a 1ª) deixa o Buddy distinguir 2+ clientes do mesmo
        /// IP (127.0.0.1 sem 2º PC): cada conexão pega a conta ainda não atrelada a uma conexão buddy ativa.</summary>
        public async Task<List<(string Account, string Nick)>> ResolveSessionsByIpAsync(string ip)
        {
            var list = new List<(string, string)>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT account, char_name FROM messenger_session WHERE ip=@ip ORDER BY login_ts DESC", c);
                cmd.Parameters.AddWithValue("@ip", ip);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    list.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
            }
            catch (Exception ex) { Log.Error("buddy", "ResolveSessionsByIpAsync({0}): {1}", ip, ex.Message); }
            return list;
        }

        /// <summary>Lista de amigos de uma conta (buddylist), resolvendo o NICK de cada amigo: buddyname (nick do
        /// messenger, se setado) > nome do char ativo > o próprio account. Chave por account (estável a nick change).</summary>
        public async Task<List<BuddyEntry>> LoadBuddyListAsync(string account)
        {
            var list = new List<BuddyEntry>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT b.Buddy, b.Category, " +
                    "COALESCE(NULLIF(g.buddyname,''), " +
                    "(SELECT ci.name FROM characterinfo ci WHERE ci.userid=g.id AND ci.used<>0 ORDER BY ci.used DESC, ci.slot LIMIT 1), " +
                    "b.Buddy) AS nick " +
                    "FROM buddylist b LEFT JOIN usergameinfo g ON g.name=b.Buddy WHERE b.Id=@acc ORDER BY b.Buddy", c);
                cmd.Parameters.AddWithValue("@acc", account);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    list.Add(new BuddyEntry(
                        r.GetString(0),
                        r.IsDBNull(2) ? r.GetString(0) : r.GetString(2),
                        r.IsDBNull(1) ? "" : r.GetString(1)));
            }
            catch (Exception ex) { Log.Error("buddy", "LoadBuddyListAsync({0}): {1}", account, ex.Message); }
            return list;
        }

        /// <summary>Remove a amizade RECÍPROCA (buddylist nos 2 sentidos) — o delete do messenger (SVC_REMOVE_BUDDY
        /// 0x3002) chega ao Buddy com o nick do amigo. Resolve o nick -> conta do amigo e apaga os 2 sentidos.
        /// Devolve false se o nick não resolve (nada a remover) ou em erro de I/O.</summary>
        public async Task<bool> RemoveBuddyByNickAsync(string ownerAccount, string buddyNick)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                string? buddyAccount = await ResolveAccountByNickAsync(c, buddyNick);
                if (buddyAccount == null) return false;
                await using var cmd = new MySqlCommand(
                    "DELETE FROM buddylist WHERE (Id=@o AND Buddy=@b) OR (Id=@b AND Buddy=@o)", c);
                cmd.Parameters.AddWithValue("@o", ownerAccount);
                cmd.Parameters.AddWithValue("@b", buddyAccount);
                int n = await cmd.ExecuteNonQueryAsync();
                Log.Ok("buddy", "remove '{0}' <-> '{1}' ({2} linha(s))", ownerAccount, buddyAccount, n);
                return true;
            }
            catch (Exception ex) { Log.Error("buddy", "RemoveBuddyByNickAsync({0},{1}): {2}", ownerAccount, buddyNick, ex.Message); return false; }
        }

        /// <summary>Resolve um nick (de rede do messenger) -> conta dona: por buddyname (nick custom) OU por
        /// nome de char. Usado no delete (o cliente manda o nick, a buddylist é chaveada por conta).</summary>
        private static async Task<string?> ResolveAccountByNickAsync(MySqlConnection c, string nick)
        {
            await using var cmd = new MySqlCommand(
                "SELECT name FROM (" +
                "  SELECT g.name, 0 AS pr FROM usergameinfo g WHERE g.buddyname=@n" +
                "  UNION ALL" +
                "  SELECT g.name, 1 AS pr FROM usergameinfo g JOIN characterinfo ci ON ci.userid=g.id WHERE ci.name=@n" +
                ") t ORDER BY pr LIMIT 1", c);
            cmd.Parameters.AddWithValue("@n", nick);
            return (await cmd.ExecuteScalarAsync()) as string;
        }
    }
}
