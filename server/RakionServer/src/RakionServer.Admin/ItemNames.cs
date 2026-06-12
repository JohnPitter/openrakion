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
}
