using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.World.Database
{
    /// <summary>Progressao do personagem: resultado de partida, ranks de stage, curva de level,
    /// alocacao de stats, Power User e char-list do char-select.</summary>
    public sealed partial class WorldDatabase
    {
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
