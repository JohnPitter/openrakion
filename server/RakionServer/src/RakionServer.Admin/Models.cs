using System.ComponentModel.DataAnnotations;

namespace RakionServer.Admin;

/// <summary>Linha da lista de contas (user + usergameinfo + cash).</summary>
public sealed record AccountRow(string Id, int GiId, int Authority, string CharName,
    long Gold, long Cash, bool PuActive, bool Ban);

public sealed record ClanRow(
    int Id, string Name, string MasterAccount, string MasterCharacter,
    int Point, int Members, uint Rank, int Country);

public sealed record ClanMemberRow(
    int GameInfoId, string AccountName, string CharacterName,
    int Point, int Rank, string TreeUpperAccount, int TreeRank, bool IsMaster);

public enum CurrencyKind : byte
{
    Gold = 0,
    Cash = 1
}

public sealed record CurrencyAdjustmentRequest(
    string AccountId, int GameInfoId, CurrencyKind Currency,
    long TargetBalance, string Reason, string OperatorId);

public sealed record CurrencyAdjustmentResult(
    long PreviousBalance, long CurrentBalance, long Delta, bool Changed);

public sealed record CurrencyAdjustmentRow(
    long Id, CurrencyKind Currency, long Delta, long BalanceBefore,
    long BalanceAfter, string Reason, string OperatorId, DateTime CreatedAt);

/// <summary>Personagem de uma conta (characterinfo).</summary>
public sealed record CharRow(int Id, string Name, int Class, int Level, bool Used, int LevelPoint);

/// <summary>Char ativo + stats (p/ o painel "Inventory" no estilo do jogo).</summary>
public sealed record CharFull(int Id, string Name, int Class, int Level, int LevelPoint, int[] Stats);

/// <summary>Slot equipado (useriteminfo) — a aparência do personagem.</summary>
public sealed record EquipSlot(int Slot, int ItemId, int Type);

/// <summary>Rótulos dos 10 stats (ordem characterinfo.hit1..maxcp), iguais aos do cliente.</summary>
public static class StatNames
{
    public static readonly string[] Labels =
    {
        "Basic attack", "Range attack", "Special attack", "Grip attack", "Cell destruction",
        "Max energy", "Max armor", "Attack speed", "Move speed", "Max cell point"
    };
}

/// <summary>Item no armazém (itembox) + o type do iteminfo (p/ cor/categoria na grade).</summary>
public sealed record BoxItemRow(int Id, int ItemId, int QSlot, int Type);

/// <summary>Definição de item (iteminfo) p/ o seletor de "adicionar item".</summary>
public sealed record ItemDef(int Id, int Type);

/// <summary>App do auto-update do launcher (fetchapp): versão atual + URLs.</summary>
public sealed record FetchAppRow(int AppId, string FileUrl, string NoticeUrl, int VerLimit);

/// <summary>Arquivo a empurrar no auto-update (fetchfile). Command M=baixar/instalar, R=remover.</summary>
public sealed record FetchFileRow(string Command, string FileDir, string FileIns, int FileVer, long FileSize);

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

/// <summary>Formulário editável da pu_config (espelha a tabela; o World relê a quente em ~15s).</summary>
public sealed class PuConfigForm
{
    [Range(0, int.MaxValue)] public int Price { get; set; } = 8000;
    [Range(0, int.MaxValue)] public int RenewalPrice { get; set; } = 6000;
    [Range(0, 9999)] public int BonusPoints { get; set; } = 5;
    [Range(0, 3650)] public int DurationDays { get; set; } = 30;
    [Range(1, 99)] public decimal ExpMult { get; set; } = 1.5m;
    [Range(1, 99)] public decimal GoldMult { get; set; } = 1.0m;
    public bool PromoActive { get; set; }
    [Range(1, 99)] public decimal PromoExpMult { get; set; } = 2.0m;
    [Range(1, 99)] public decimal PromoGoldMult { get; set; } = 1.0m;
    public DateTime? PromoStart { get; set; }
    public DateTime? PromoEnd { get; set; }
}

/// <summary>Linha editável de um catalisador do refino (enchant_catalyzer).</summary>
public sealed class EnchantCatalyzerForm
{
    public int CatalyzerId { get; set; }
    public string Name { get; set; } = "";
    [Range(0, 1)] public decimal BaseSuccess { get; set; } = 0.90m;   // chance no +0
    [Range(0, 1)] public decimal Decay { get; set; } = 0.07m;         // queda por nível
    [Range(0, 15)] public int LevelCap { get; set; } = 14;            // nível máx. da arma aceito
}

/// <summary>Formulário do refino: globais (enchant_config) + catalisadores (enchant_catalyzer). O World relê a
/// quente (~15s) — salvar aplica sem reiniciar. EventMult vale p/ todos; PuMult só p/ quem tem Power User.</summary>
public sealed class EnchantConfigForm
{
    [Range(0, 99)] public decimal EventMult { get; set; } = 1.0m;     // multiplicador GLOBAL (eventos)
    [Range(0, 99)] public decimal PuMult { get; set; } = 1.0m;        // multiplicador só com Power User
    [Range(0, 1)] public decimal JewelFloor { get; set; } = 0.05m;    // piso por joia tipo 3 (Charm)
    [Range(0, 1)] public decimal JewelBonus { get; set; } = 0.03m;    // bônus por joia tipo 1/2
    [Range(0, 1)] public decimal FloorMin { get; set; } = 0.05m;
    [Range(0, 1)] public decimal CeilMax { get; set; } = 0.98m;
    [Range(0, 1)] public decimal DowngradeLo { get; set; } = 0.12m;   // risco de cair em +3..+5
    [Range(0, 1)] public decimal DowngradeHi { get; set; } = 0.30m;   // risco de cair em +6+
    public List<EnchantCatalyzerForm> Catalyzers { get; set; } = new();
}
