using System.ComponentModel.DataAnnotations;

namespace RakionServer.Admin;

/// <summary>Linha da lista de contas (user + usergameinfo + cash).</summary>
public sealed record AccountRow(string Id, int GiId, int Authority, string CharName,
    long Gold, long Cash, bool PuActive, bool Ban);

/// <summary>Personagem de uma conta (characterinfo).</summary>
public sealed record CharRow(int Id, string Name, int Class, int Level, bool Used, int LevelPoint);

/// <summary>Item no armazém (itembox).</summary>
public sealed record BoxItemRow(int Id, int ItemId, int QSlot);

/// <summary>Definição de item (iteminfo) p/ o seletor de "adicionar item".</summary>
public sealed record ItemDef(int Id, string Name);

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
