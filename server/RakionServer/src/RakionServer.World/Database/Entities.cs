using System;

namespace RakionServer.World.Database
{
    /// <summary>POCOs das tabelas do db `rakion` (schema v258). Um por entidade de jogo.</summary>

    /// <summary>Tabela `characterinfo` — personagem do jogador.</summary>
    public sealed class CharacterInfo
    {
        public int Id;
        public int UserId;
        public string Name = "";
        public bool Used;
        public byte Class;
        public byte Level = 1;
        public int Win, Lose, Draw, Exp;
        public byte LevelPoint;
        public byte Slot;
        public int TotalRank, ClassRank;
    }

    /// <summary>Tabela `useriteminfo` — item possuido por um personagem.</summary>
    public sealed class UserItem
    {
        public int Id;
        public int UserId;
        public int CharacterId;
        public int ItemId;
        public int ItemSn;
        public byte SnType = 3;
        public byte Level = 1;
        public int LimitTime;
        public byte Slot = 1;
        public long Exp;
    }

    /// <summary>Tabela `iteminfo` — definicao/catalogo de um item (loja, stats).</summary>
    public sealed class ItemDef
    {
        public int Id;
        public byte Type;
        public byte Class;
        public byte Level;
        public byte Shop;
        public int Gold;
        public int Cash;
        public int Hit1, Hit2, Hit3, Hit4, CHit, Ap, Hp, MaxCp, Power;
    }

    /// <summary>Tabela `claninfo` — cla/guild.</summary>
    public sealed class ClanInfo
    {
        public int Id;
        public int MasterId;
        public string MasterName = "";
        public string Name = "";
        public int Point;
        public short Members;
        public uint Rank;
        public short Country;
    }
}
