using System;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    /// <summary>
    /// Acesso ao DB do domínio MESSENGER/amigos (botão F9), fatiado do <see cref="WorldDatabase"/> por tamanho:
    /// identidade (messenger_session, gravada no login e apagada no logout — o login do Buddy é cifrado, então a
    /// identidade vem daqui e o Buddy resolve por IP), amizade (buddylist recíproca, idempotente) e nick do
    /// messenger (usergameinfo.buddyname). A escrita da amizade mora aqui porque o AddBuddy do cliente é MUDO.
    /// </summary>
    public sealed partial class WorldDatabase
    {
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

        /// <summary>Grava a identidade do messenger (messenger_session) no LOGIN do world. O login do Buddy é
        /// CIFRADO (AES, 208B opacos) e não carrega o nick, então o Buddy não sabe quem conectou; o World — que
        /// conhece a conta — registra account+nick+IP e o Buddy resolve a conexão por IP (ResolveAccountByIp).
        /// REPLACE = 1 sessão por conta.</summary>
        public async Task UpsertMessengerSessionAsync(string account, string charName, string ip)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "REPLACE INTO messenger_session (account, char_name, ip, login_ts) VALUES (@a,@n,@ip,NOW())", c);
                cmd.Parameters.AddWithValue("@a", account);
                cmd.Parameters.AddWithValue("@n", charName ?? "");
                cmd.Parameters.AddWithValue("@ip", ip ?? "");
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "UpsertMessengerSessionAsync('{0}'): {1}", account, ex.Message); }
        }

        /// <summary>Apaga a identidade do messenger (messenger_session) no LOGOUT do world.</summary>
        public async Task DeleteMessengerSessionAsync(string account)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("DELETE FROM messenger_session WHERE account=@a", c);
                cmd.Parameters.AddWithValue("@a", account);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "DeleteMessengerSessionAsync('{0}'): {1}", account, ex.Message); }
        }

        /// <summary>Persiste a amizade RECÍPROCA (buddylist nos 2 sentidos): o AddBuddy do cliente é MUDO (máscara
        /// +0x140d4 bit12=0 -> não emite SVC_ADD_BUDDY ao buddy), então o WORLD — que no 0x19 conhece dono e alvo —
        /// grava. Idempotente (INSERT só se ausente, sem depender de unique key do dump) e sem auto-amizade. Chaves
        /// por ACCOUNT (estável a nick change). Devolve false só em erro de I/O.</summary>
        public async Task<bool> AddBuddyReciprocalAsync(string ownerAccount, string buddyAccount, string category = "")
        {
            if (string.Equals(ownerAccount, buddyAccount, StringComparison.OrdinalIgnoreCase)) return true; // sem auto-amizade
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await InsertBuddyIfAbsent(c, ownerAccount, buddyAccount, category);
                await InsertBuddyIfAbsent(c, buddyAccount, ownerAccount, category);
                return true;
            }
            catch (Exception ex) { Log.Error("db", "AddBuddyReciprocalAsync('{0}','{1}'): {2}", ownerAccount, buddyAccount, ex.Message); return false; }
        }

        private static async Task InsertBuddyIfAbsent(MySqlConnection c, string owner, string buddy, string category)
        {
            await using var cmd = new MySqlCommand(
                "INSERT INTO buddylist (Id, Buddy, Category) SELECT @o,@b,@cat FROM DUAL " +
                "WHERE NOT EXISTS (SELECT 1 FROM buddylist WHERE Id=@o AND Buddy=@b)", c);
            cmd.Parameters.AddWithValue("@o", owner);
            cmd.Parameters.AddWithValue("@b", buddy);
            cmd.Parameters.AddWithValue("@cat", category ?? "");
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Sincroniza o nick do messenger (usergameinfo.buddyname) com o nome do nick change (0x15
        /// CharacterChangeBuddyName), pela conta (usergameinfo.id da sessão).</summary>
        public async Task UpdateBuddyNameAsync(int gameInfoId, string buddyName)
        {
            try
            {
                await using var c = new MySqlConnection(_conn);
                await c.OpenAsync();
                await using var cmd = new MySqlCommand("UPDATE usergameinfo SET buddyname=@n WHERE id=@id", c);
                cmd.Parameters.AddWithValue("@n", buddyName ?? "");
                cmd.Parameters.AddWithValue("@id", gameInfoId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Log.Error("db", "UpdateBuddyNameAsync({0}): {1}", gameInfoId, ex.Message); }
        }
    }
}
