using System.ComponentModel.DataAnnotations;

namespace RakionServer.Admin;

/// <summary>Linha da lista de contas (user + usergameinfo + cash).</summary>
public sealed record AccountRow(string Id, int GiId, int Authority, string CharName,
    long Gold, long Cash, bool PuActive, bool Ban);

/// <summary>Personagem de uma conta (characterinfo).</summary>
public sealed record CharRow(int Id, string Name, int Class, int Level, bool Used, int LevelPoint);

/// <summary>Item no armazém (itembox) + o type do iteminfo (p/ cor/categoria na grade).</summary>
public sealed record BoxItemRow(int Id, int ItemId, int QSlot, int Type);

/// <summary>Definição de item (iteminfo) p/ o seletor de "adicionar item".</summary>
public sealed record ItemDef(int Id, int Type);

/// <summary>Categoria + cor por type do iteminfo (Rakion não tem ícone 2D — os do jogo são
/// render 3D em runtime; aqui simulamos a grade colorindo por tipo).</summary>
public static class ItemKind
{
    public static string Label(int type) => type switch
    {
        >= 0 and <= 5 => "Equip",
        6 or 7 => "Equip+",
        8 => "Transform",
        10 => "Arma",
        13 => "Poção",
        -1 => "—",
        _ => "Tipo " + type
    };

    // cor determinística por tipo (mesma família = mesma cor)
    public static string Color(int type) => type switch
    {
        -1 => "#33384400",
        13 => "#1f6b46",
        10 => "#8a4528",
        8 => "#4a4f5a",
        _ => $"hsl({(type * 41 + 205) % 360} 40% 38%)"
    };
}

/// <summary>Formulário editável da pu_config (espelha a tabela; o World relê no boot).</summary>
public sealed class PuConfigForm
{
    [Range(0, int.MaxValue)] public int Price { get; set; } = 8000;
    [Range(0, 9999)] public int BonusPoints { get; set; } = 51;
    [Range(0, 3650)] public int DurationDays { get; set; } = 30;
    [Range(1, 99)] public decimal ExpMult { get; set; } = 1.5m;
    [Range(1, 99)] public decimal GoldMult { get; set; } = 1.5m;
    public bool PromoActive { get; set; }
    [Range(1, 99)] public decimal PromoExpMult { get; set; } = 2.0m;
    [Range(1, 99)] public decimal PromoGoldMult { get; set; } = 2.0m;
    public DateTime? PromoStart { get; set; }
    public DateTime? PromoEnd { get; set; }
}
