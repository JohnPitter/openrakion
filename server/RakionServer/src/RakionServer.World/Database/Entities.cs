using System;
using System.Collections.Generic;

namespace RakionServer.World.Database
{
    public enum CharacterCreateStatus : byte
    {
        Success = 0,
        Failed = 1,
        SlotOccupied = 2,
        InvalidClass = 3,
        DuplicateName = 4
    }

    public sealed record CharacterCreateResult(CharacterCreateStatus Status, CharacterInfo? Character = null);

    public enum CharacterSelectStatus : byte
    {
        Success = 0,
        SystemError = 1,
        NotFound = 2
    }

    public sealed record CharacterSelectResult(
        CharacterSelectStatus Status, CharacterInfo? Character = null);

    public enum CharacterStateClearStatus : byte
    {
        Success = 0,
        InsufficientCash = 1,
        NoAllocatedStats = 2,
        Failed = 11,
        InvalidCouponItem = 0x14,
        CouponNotForCash = 0x15,
        CouponDefinitionMissing = 0x16
    }

    public sealed record CharacterStateClearResult(
        CharacterStateClearStatus Status, int Cash = 0, ushort LevelPoint = 0,
        ushort PowerLevelPoint = 0, int[]? Presents = null);

    public enum CharacterRenameStatus : byte
    {
        Success = 0,
        DuplicateName = 1,
        InsufficientCash = 2,
        Failed = 11,
        InvalidCouponItem = 0x14,
        CouponNotForCash = 0x15,
        CouponDefinitionMissing = 0x16
    }

    public sealed record CharacterRenameResult(
        CharacterRenameStatus Status, int Cash = 0, string Name = "", int[]? Presents = null);

    public sealed record CharacterPayment(byte Type, ushort Slot = 0, int StorageRowId = 0, int ItemId = 0)
    {
        public bool UsesCoupon => Type == 1;
    }

    public enum InventoryEntitlement : byte
    {
        Bag,
        CharacterSlot
    }

    public enum EntitlementPurchaseStatus : byte
    {
        Success = 0,
        Failed = 1,
        InProgress = 2,
        LimitReached = 3,
        InsufficientCash = 4,
        InvalidCouponItem = 0x14,
        CouponNotForCash = 0x15,
        CouponDefinitionMissing = 0x16
    }

    public sealed record EntitlementPurchaseResult(
        EntitlementPurchaseStatus Status, int Gold = 0, int Cash = 0, byte Value = 0,
        int CouponItemId = 0, int[]? Presents = null);

    public sealed record PotionSlotPurchaseResult(
        EntitlementPurchaseStatus Status, int Gold = 0, int Cash = 0,
        byte PotionSlots = 0, int[]? Presents = null);

    public sealed record FieldPotionConsumeRequest(
        int UserId, int CharacterId, byte Cell, int ItemId);

    public sealed record FieldPotionConsumeResult(bool Success, int RemainingCount = 0);

    public enum StageEntitlementStatus : byte
    {
        Success = 0,
        Failed = 1,
        InsufficientCash = 2,
        NotEligible = 3
    }

    public sealed record StageRankClearResult(
        StageEntitlementStatus Status, int Gold = 0, int Cash = 0, int[]? Presents = null);

    public sealed record StageLevelFreeResult(
        StageEntitlementStatus Status, int Gold = 0, int Cash = 0,
        uint MinuteMarker = 0, int[]? Presents = null);

    public enum PresentPeekStatus : byte { Success = 0, Empty = 1 }

    public sealed record PresentPeekResult(
        PresentPeekStatus Status, int PendingId = 0, int ItemId = 0, ushort ItemOption = 0);

    public enum PresentAcceptStatus : byte
    {
        Success = 0,
        NotFirst = 1,
        Empty = 2,
        SlotOccupied = 3,
        Failed = 4
    }

    public sealed record PresentAcceptResult(
        PresentAcceptStatus Status, int StorageRowId = 0, int ItemId = 0,
        ushort Slot = 0, byte Level = 0, int ResponseValue = 0);

    public enum PresentDisposeStatus : byte
    {
        Success = 0,
        NotFirst = 1,
        Empty = 2,
        Failed = 4
    }

    public sealed record PresentDisposeResult(PresentDisposeStatus Status);

    public enum CharacterDeleteResult : byte
    {
        Success = 0,
        Failed = 1,
        NotFound = 2,
        TooYoung = 3,
        ClanAuthority = 4,
        InvalidKey = 5,
        InvalidEmail = 6,
        DeleteKeySent = 7,
        ClanMaster = 8,
        ActiveCharacter = 9
    }

    public sealed record CharacterDeleteOutcome(
        CharacterDeleteResult Result,
        string AccountName = "",
        string CharacterName = "",
        string Email = "",
        string DeleteKey = "");

    public enum BuddyNameChangeResult
    {
        Success,
        Duplicate,
        NotFound,
        Failed
    }

    /// <summary>POCOs das tabelas do db `rakion` (schema v258). Um por entidade de jogo.</summary>

    /// <summary>Tabela `characterinfo` — personagem do jogador.</summary>
    public sealed class CharacterInfo
    {
        public int Id;
        public int UserId;
        public string Name = "";
        public bool Used;
        public byte Auth;
        public byte Class;
        public byte Level = 1;
        public int Win, Lose, Draw, Exp;
        public byte LevelPoint;
        public byte Slot;
        public byte PotionSlots = 3;
        // stats alocados (characterinfo): hit1..maxcp = Basic/Range/Special/Grip attack, Cell destruction,
        // Max energy, Max armor, Attack speed, Move speed, Max cell point. Indice 0..9 da alocacao 0x33.
        public byte Hit1, Hit2, Hit3, Hit4, Chit, Hp, Ap, AttackSpeed, Speed, Maxcp;
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

    public sealed record StorageItem(int Id, int ItemId, int Level, int Cell);

    public sealed record StorageGrant(int ItemId, int? Cell);

    public sealed record StoragePurchaseIntent(
        int ItemId, bool PayGold, int BasePrice, byte PaymentType, ushort PaymentValue);

    public sealed record StoragePurchaseCommand(
        int UserId, string AccountId, int CharacterId, byte CharacterClass,
        int CatalogItemId, bool PayGold, int BasePrice, int StorageCapacity,
        CharacterPayment Payment, IReadOnlyList<StorageGrant> Grants);

    public sealed record StorageSaleCommand(
        int UserId, int CharacterId, int RowId, int ItemId, int Cell,
        bool RemoveStack, int GoldCredit);

    public enum StorageMutationStatus : byte
    {
        Success = 0,
        InsufficientFunds = 1,
        NoSpace = 2,
        Failed = 3,
        InvalidCouponItem = 0x14,
        CouponNotForCurrency = 0x15,
        CouponDefinitionMissing = 0x16
    }

    public sealed record StoragePurchaseResult(
        StorageMutationStatus Status, int Balance = 0,
        StorageGrant[]? Grants = null, int[]? RowIds = null, int[]? Presents = null,
        int CouponRowId = 0, int CouponItemId = 0, int CouponDiscount = 0);

    public sealed record StorageSaleResult(
        StorageMutationStatus Status, int Gold = 0, int Credit = 0,
        int ItemHandle = 0, byte Level = 0, long Experience = 0);

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
