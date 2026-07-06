using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;

namespace RakionServer.World
{
    /// <summary>
    /// Catálogo de itens (iteminfo): cache imutável itemId→<see cref="ItemDef"/> carregado UMA vez no boot.
    /// Serviço AUTO-CONTIDO — depende só do <see cref="WorldDatabase"/> (na carga); consultas são read-only.
    /// Extraído do <see cref="WorldServer"/> p/ isolar o acoplamento (preço Gold/Cash, tipo, composição de set).
    /// </summary>
    public sealed class ItemCatalog
    {
        private readonly WorldDatabase _db;
        private Dictionary<int, ItemDef> _defs = new();

        public ItemCatalog(WorldDatabase db) => _db = db;

        /// <summary>Carrega o catálogo uma vez (iteminfo). Chamado no boot (WorldServer.StartAsync).</summary>
        public async Task LoadAsync()
        {
            var list = await _db.LoadItemDefsAsync();
            var map = new Dictionary<int, ItemDef>(list.Count);
            foreach (var d in list) map[d.Id] = d;
            _defs = map;
            Log.Ok("shop", "catalogo de itens carregado: {0} definicoes", map.Count);
        }

        /// <summary>Definição do item (preço Gold/Cash, tipo, stats). null = não catalogado.</summary>
        public ItemDef? Find(int itemId) => _defs.TryGetValue(itemId, out var d) ? d : null;

        /// <summary>
        /// O item pode ser DESENHADO no grid do armazem (box)? O crash do painel no Previous que motivava o
        /// filtro antigo (só type&lt;=5) JÁ foi resolvido (acks 0x2c/0x2d fiéis ao original), então gear +
        /// materiais/consumiveis/cash pintam. type 8 (cell/creature) LIBERADO no grid (captura do 0x307 real →
        /// HIT×N nativo); RISCO ACEITO no teste (pode pintar invisível). Reverter p/ `Find(itemId)?.Type != 8`
        /// se instabilizar. Item não catalogado = não desenhável.
        /// </summary>
        public bool IsBoxDisplayable(int itemId) => Find(itemId) != null;

        /// <summary>Item é um SET (type 10) — um BUNDLE de peças, não uma peça equipável direta.</summary>
        public bool IsSet(int itemId) => Find(itemId)?.Type == 10;

        /// <summary>Composição de um SET (type 10): as colunas hit1-4/chit/ap do iteminfo guardam os itemIds
        /// dos membros (1 por slot de gear, faixa 0-5) — confirmado: 9012 -> 1009/1109/1209/1309/1409/1509.
        /// Fonte ÚNICA da composição. Só retorna membros que são itens válidos do catálogo. Vazio se não for set.</summary>
        public IReadOnlyList<int> ExpandSetMembers(int setItemId)
        {
            var d = Find(setItemId);
            if (d == null || d.Type != 10) return Array.Empty<int>();
            var members = new List<int>(6);
            foreach (var m in new[] { d.Hit1, d.Hit2, d.Hit3, d.Hit4, d.CHit, d.Ap })
                if (m > 0 && Find(m) != null) members.Add(m);
            return members;
        }
    }
}
