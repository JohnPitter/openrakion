using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Acesso ao MariaDB (db `rakion`) do Buddy server. O messenger e' um processo separado do
    /// world, mas COMPARTILHA o banco — e' por ele que o buddy descobre a IDENTIDADE de cada
    /// conexao (o login do messenger e' cifrado AES-ECB, ilegivel — captura confirmou 208B opacos)
    /// e carrega a lista de amigos persistida.
    ///
    /// Identidade: o WORLD grava `messenger_session(account, ip)` no login; o buddy resolve pelo IP
    /// de origem da conexao TCP (mesma maquina -> mesmo IP no world e no buddy). Para 2 clientes no
    /// mesmo IP (loopback) usa a sessao mais recente; um 2o player em container tem IP proprio, sem
    /// ambiguidade. Reforco futuro: a fingerprint estavel de 48B do SVC_LOGIN (mapear fp->account).
    /// </summary>
    public sealed class BuddyDatabase
    {
        private readonly string _conn;

        public BuddyDatabase(string? conn = null) =>
            _conn = conn
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Rakion")
                ?? Environment.GetEnvironmentVariable("RAKION_DB")
                ?? "Server=127.0.0.1;Port=3306;Database=rakion;Uid=root;Pwd=123456;";

        /// <summary>Resolve a identidade (account) de uma conexao pelo IP de origem: a sessao de world
        /// ativa mais recente daquele IP. null se nao houver (cliente nao logado no world).</summary>
        public async Task<string?> ResolveAccountByIpAsync(string ip)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT account FROM messenger_session WHERE ip=@ip ORDER BY login_ts DESC LIMIT 1", c);
                cmd.Parameters.AddWithValue("@ip", ip);
                return (await cmd.ExecuteScalarAsync()) as string;
            }
            catch (Exception ex) { Log.Error("buddydb", "ResolveAccountByIp({0}): {1}", ip, ex.Message); return null; }
        }

        /// <summary>Nick do char (characterinfo.name, used=1) de um account — display do proprio usuario.</summary>
        public async Task<string?> GetNickByAccountAsync(string account)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT ci.name FROM characterinfo ci JOIN usergameinfo gi ON ci.userid=gi.id " +
                    "WHERE gi.name=@a AND ci.used=1 LIMIT 1", c);
                cmd.Parameters.AddWithValue("@a", account);
                return (await cmd.ExecuteScalarAsync()) as string;
            }
            catch (Exception ex) { Log.Error("buddydb", "GetNickByAccount({0}): {1}", account, ex.Message); return null; }
        }

        /// <summary>Carrega a lista de amigos do dono (buddylist.Id=@owner): para cada amigo, o account
        /// (chave estavel, do 0x19) e o nick do char (display da janela do messenger).</summary>
        public async Task<List<BuddyEntry>> LoadBuddiesAsync(string ownerAccount)
        {
            var list = new List<BuddyEntry>();
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT bl.Buddy, COALESCE((SELECT ci.name FROM characterinfo ci " +
                    "  JOIN usergameinfo gi ON ci.userid=gi.id WHERE gi.name=bl.Buddy AND ci.used=1 LIMIT 1), " +
                    "  bl.Buddy) AS nick, COALESCE(bl.Category, '') AS grp " +
                    "FROM buddylist bl WHERE bl.Id=@owner", c);
                cmd.Parameters.AddWithValue("@owner", ownerAccount);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    list.Add(new BuddyEntry(r.GetString(0), r.IsDBNull(1) ? r.GetString(0) : r.GetString(1), r.GetString(2)));
            }
            catch (Exception ex) { Log.Error("buddydb", "LoadBuddies({0}): {1}", ownerAccount, ex.Message); }
            return list;
        }
    }

    /// <summary>Um amigo na lista do messenger: Account (chave do 0x19) + Nick (char, display + chave de
    /// rede) + Category (grupo da buddylist).</summary>
    public readonly record struct BuddyEntry(string Account, string Nick, string Category);
}
