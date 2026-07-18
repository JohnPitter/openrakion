using System;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>Motor de refino/enchant (0x74) e catalogo de itens (iteminfo/sets/box-display).</summary>
    public sealed partial class WorldServer
    {
        /// <summary>Aplica o UPGRADE do refino (handler 0x74 = clique de upgrade). SERVER-AUTHORITATIVE: este build do
        /// worldserv DESCARTAVA o roll de FUN_0040c310 (o cliente fica em "Upgrading Now" esperando o resultado), então
        /// reconstruímos o comportamento pretendido — valida (CUser::CheckEnchantReinforce), ROLA a probabilidade,
        /// aplica o delta de nível na arma (persiste itembox.level) e consome catalyzer + materiais. Regra do motor de
        /// refino — fora do handler de rede. Devolve o result code (0=+1, 1=nada, 2=-1; 6/7/8 = erro de validação).</summary>
        public byte ApplyEnchant(ClientSession s, byte weaponSlot, byte catalyzerSlot,
            System.Collections.Generic.IReadOnlyList<byte> materialSlots)
        {
            // VALIDAÇÃO (FUN_0040c310): arma presente e <8000, catalyzer 0x32c9..0x32cd, materiais 0x36b1..0x36b3,
            // caps de nível. Erro -> 6/7/8 (o cliente mostra "não dá"), sem mexer no estado.
            int weaponId = BoxItemAt(s, weaponSlot);
            int catId = BoxItemAt(s, catalyzerSlot);
            if (weaponId == 0 || weaponId >= 8000) return 6;
            if (!EnchantConfig.TryGetCatalyzer(catId, out var cat)) return 7;   // catalisador desconhecido
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
            byte result = RollEnchant(catId, curLevel, j1, j2, j3, s.PuActive);
            int delta = EnchantDelta(result);
            int newLevel = System.Math.Clamp(curLevel + delta, 0, 15);
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
        private byte RollEnchant(int catalyzerId, int curLevel, int j1, int j2, int j3, bool puActive)
        {
            double p0 = EnchantConfig.SuccessChance(catalyzerId, curLevel, j1, j2, j3, puActive);
            if (_rng.NextDouble() < p0) return 0;   // SUCESSO +1
            double downgrade = (1.0 - p0) * EnchantConfig.DowngradeFactor(curLevel);
            return _rng.NextDouble() < downgrade ? (byte)2 : (byte)1;   // 2=-1 (downgrade) ou 1=nada
        }

        private static int EnchantDelta(byte resultCode) => resultCode switch
        {
            0 => +1, 2 => -1, 3 => -2, 4 => -3, _ => 0,
        };

        private readonly System.Random _rng = new();

        private System.Collections.Generic.Dictionary<int, Database.ItemDef> _itemDefs = new();
        /// <summary>Catalogo de itens (iteminfo) carregado no boot. Preco Gold/Cash por itemId.</summary>
        public Database.ItemDef? FindItemDef(int itemId) => _itemDefs.TryGetValue(itemId, out var d) ? d : null;

        /// <summary>
        /// O item pode ser DESENHADO no grid do armazem (box)? Só "gear" = tipos 0-5 (slots de equipamento,
        /// Class bitmask 1-16, ids 1xxx-5xxx). Tipos 6-14 (transform=8, lotto=11, especial=13, etc., todos
        /// Class 31) NÃO têm ícone de box no cliente GG-removido -> renderizam invisíveis e crasham o painel
        /// ao reconstruir (botão "Previous"). Esses ainda são comprados/persistidos no itembox, só não pintam
        /// no grid. Catálogo do cliente: o box-visual (FUN_004774e0) só trata especial o tipo 0x0c, e o gear
        /// tipo 0 foi confirmado em jogo; tipos 8/13 foram confirmados invisíveis+crash.
        /// </summary>
        public bool IsBoxDisplayable(int itemId)
        {
            var d = FindItemDef(itemId);
            if (d == null) return false;
            // Gear (0-5) + materiais/consumiveis/cash (6,7,9-14, ex: Mithril 13001 type 13) têm ícone de
            // box e pintam normalmente. O crash do painel no Previous que motivava o filtro antigo (só
            // type<=5) JÁ foi resolvido (acks 0x2c/0x2d fiéis ao original). Só o type 8 (transform) fica
            // fora — não tem ícone de box no cliente GG-removido (renderiza invisível).
            return d.Type != 8;
        }

        /// <summary>Item é um SET (type 10) — um BUNDLE de peças, não uma peça equipável direta.</summary>
        public bool IsSet(int itemId) => FindItemDef(itemId)?.Type == 10;

        /// <summary>Composição de um SET (type 10): as colunas hit1-4/chit/ap do iteminfo guardam os itemIds
        /// dos membros (1 por slot de gear, faixa 0-5) — confirmado: 9012 -> 1009/1109/1209/1309/1409/1509.
        /// Fonte ÚNICA da composição. Só retorna membros que são itens válidos do catálogo (um valor que não
        /// resolve em item é stat, não membro -> filtrado). Vazio se não for set ou sem membros válidos.</summary>
        public System.Collections.Generic.IReadOnlyList<int> ExpandSetMembers(int setItemId)
        {
            var d = FindItemDef(setItemId);
            if (d == null || d.Type != 10) return System.Array.Empty<int>();
            var members = new System.Collections.Generic.List<int>(6);
            foreach (var m in new[] { d.Hit1, d.Hit2, d.Hit3, d.Hit4, d.CHit, d.Ap })
                if (m > 0 && FindItemDef(m) != null) members.Add(m);
            return members;
        }

        /// <summary>Carrega o catalogo de itens uma vez (iteminfo).</summary>
        public async Task LoadItemDefsCacheAsync()
        {
            var list = await _db.LoadItemDefsAsync();
            var map = new System.Collections.Generic.Dictionary<int, Database.ItemDef>(list.Count);
            foreach (var d in list) map[d.Id] = d;
            _itemDefs = map;
            Log.Ok("shop", "catalogo de itens carregado: {0} definicoes", map.Count);
        }
    }
}
