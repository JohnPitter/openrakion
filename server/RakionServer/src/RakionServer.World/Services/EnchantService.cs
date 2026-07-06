using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Motor de REFINO/enchant (handler 0x74): valida, ROLA a probabilidade server-authoritative, aplica o delta
    /// de nível na arma e consome catalyzer + materiais. Regra de NEGÓCIO extraída do <see cref="WorldServer"/> —
    /// opera sobre o box da <see cref="ClientSession"/> e persiste via <see cref="WorldDatabase"/>. A
    /// <see cref="EnchantConfig"/> é recarregada a quente pelo servidor; o serviço lê a VIGENTE via provider.
    /// </summary>
    public sealed class EnchantService
    {
        private readonly WorldDatabase _db;
        private readonly Func<EnchantConfig> _config;   // config recarregável (ConfigReloadLoop) — lê a vigente
        private readonly Random _rng = new();

        public EnchantService(WorldDatabase db, Func<EnchantConfig> config)
        {
            _db = db;
            _config = config;
        }

        /// <summary>Aplica o UPGRADE do refino (handler 0x74 = clique de upgrade). SERVER-AUTHORITATIVE: este build do
        /// worldserv DESCARTAVA o roll de FUN_0040c310 (o cliente fica em "Upgrading Now"), então reconstruímos o
        /// comportamento — valida (CUser::CheckEnchantReinforce), ROLA a probabilidade, aplica o delta de nível na
        /// arma (persiste itembox.level) e consome catalyzer + materiais. Devolve o result code (0=+1, 1=nada, 2=-1;
        /// 6/7/8 = erro de validação).</summary>
        public byte ApplyEnchant(ClientSession s, byte weaponSlot, byte catalyzerSlot,
            IReadOnlyList<byte> materialSlots)
        {
            var cfg = _config();
            // VALIDAÇÃO (FUN_0040c310): arma presente e <8000, catalyzer 0x32c9..0x32cd, materiais 0x36b1..0x36b3,
            // caps de nível. Erro -> 6/7/8 (o cliente mostra "não dá"), sem mexer no estado.
            int weaponId = BoxItemAt(s, weaponSlot);
            int catId = BoxItemAt(s, catalyzerSlot);
            if (weaponId == 0 || weaponId >= 8000) return 6;
            if (!cfg.TryGetCatalyzer(catId, out var cat)) return 7;   // catalisador desconhecido
            int curLevel = weaponSlot < s.BoxLevel.Count ? s.BoxLevel[weaponSlot] : 0;
            if (curLevel >= 15) return 6;
            if (curLevel > cat.LevelCap) return 8;             // arma acima do teto do catalisador (Mithril +4, etc.)
            int j1 = 0, j2 = 0, j3 = 0;
            foreach (byte ms in materialSlots)
            {
                int mid = BoxItemAt(s, ms);
                if (mid == 0x36b1) j1++;
                else if (mid == 0x36b2) j2++;
                else if (mid == 0x36b3) j3++;
                else return 7;
            }

            // ROLL server-authoritative (a fórmula de probabilidade roda no servidor) -> delta de nível.
            // s.PuActive entra no roll: a EnchantConfig aplica o multiplicador de Power User (e o de evento).
            byte result = RollEnchant(cfg, catId, curLevel, j1, j2, j3, s.PuActive);
            int delta = EnchantDelta(result);
            int newLevel = Math.Clamp(curLevel + delta, 0, 15);
            if (weaponSlot < s.BoxLevel.Count) s.BoxLevel[weaponSlot] = newLevel;
            int wRow = weaponSlot < s.BoxRowId.Count ? s.BoxRowId[weaponSlot] : 0;
            if (wRow > 0) _ = _db.UpdateItemBoxLevelAsync(wRow, newLevel);          // persiste o +N

            // CONSOME catalyzer + materiais (some do box; remoção persistida pela linha EXATA do itembox)
            Consume(s, catalyzerSlot);
            foreach (byte ms in materialSlots) Consume(s, ms);

            Log.Ok("enchant", "[{0}] refino: arma slot {1} +{2}->+{3} (result={4}); catalyzer {5:x} + {6} joia(s) [j1={7} j2={8} j3={9}] consumidos",
                s.Slot, weaponSlot, curLevel, newLevel, result, catId, materialSlots.Count, j1, j2, j3);
            return result;
        }

        /// <summary>Lê o itemId de uma célula do box (0 = vazia/fora de faixa).</summary>
        private static int BoxItemAt(ClientSession s, byte slot) => slot < s.BoxItems.Count ? s.BoxItems[slot] : 0;

        /// <summary>Esvazia a célula e persiste a remoção da linha EXATA do itembox.</summary>
        private void Consume(ClientSession s, byte slot)
        {
            var (_, rowId) = s.ClearBoxCell(slot);
            if (rowId > 0) _ = _db.DeleteItemBoxByIdAsync(rowId);
        }

        /// <summary>Roleta do refino: a probabilidade de sucesso vem da <see cref="EnchantConfig"/> (banco —
        /// coeficientes por catalisador + multiplicadores de evento/PU; estrutura do polinômio fiel ao FUN_0040c310).
        /// Aqui só sorteamos sucesso vs falha e, na falha, se vira downgrade (-1) ou nada. Destroy (result 5) é
        /// neutralizado neste build. Devolve o result code (0=+1, 1=nada, 2=-1).</summary>
        private byte RollEnchant(EnchantConfig cfg, int catalyzerId, int curLevel, int j1, int j2, int j3, bool puActive)
        {
            double p0 = cfg.SuccessChance(catalyzerId, curLevel, j1, j2, j3, puActive);
            if (_rng.NextDouble() < p0) return 0;   // SUCESSO +1
            double downgrade = (1.0 - p0) * cfg.DowngradeFactor(curLevel);
            return _rng.NextDouble() < downgrade ? (byte)2 : (byte)1;   // 2=-1 (downgrade) ou 1=nada
        }

        private static int EnchantDelta(byte resultCode) => resultCode switch
        {
            0 => +1, 2 => -1, 3 => -2, 4 => -3, _ => 0,
        };
    }
}
