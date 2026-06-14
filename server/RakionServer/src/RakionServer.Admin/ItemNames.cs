namespace RakionServer.Admin;

/// <summary>
/// Mapa id → nome do item, extraído do `items.dat` do cliente (item_names.tsv, copiado ao output).
/// Carregado uma vez no boot. O iteminfo do DB não tem nome; estes vêm dos labels do client.
/// </summary>
public sealed class ItemNames
{
    private readonly Dictionary<int, string> _map = new();

    public ItemNames()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "item_names.tsv");
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line.AsSpan(0, tab), out int id))
                    _map[id] = line[(tab + 1)..].Trim();
            }
        }
        catch { }
    }

    public string? Get(int id) => _map.TryGetValue(id, out var v) ? v : null;
    public int Count => _map.Count;

    /// <summary>Ids cujo nome contém <paramref name="term"/> (case-insensitive). Vazio se term vazio.</summary>
    public IReadOnlyList<int> Search(string? term, int limit = 200)
    {
        var hits = new List<int>();
        if (string.IsNullOrWhiteSpace(term)) return hits;
        foreach (var kv in _map)
        {
            if (kv.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(kv.Key);
                if (hits.Count >= limit) break;
            }
        }
        return hits;
    }
}
