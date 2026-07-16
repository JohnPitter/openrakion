using System;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class InventoryEntitlementTests
    {
        [Theory]
        [InlineData(InventoryEntitlement.Bag, 8000, 10006, 3)]
        [InlineData(InventoryEntitlement.CharacterSlot, 12000, 10007, 6)]
        public void Rules_MatchWorldServerConstants(
            InventoryEntitlement entitlement, int price, int productId, byte maximum)
        {
            Assert.Equal(price, InventoryEntitlementRules.Price(entitlement));
            Assert.Equal(productId, InventoryEntitlementRules.ProductId(entitlement));
            Assert.Equal(maximum, InventoryEntitlementRules.Maximum(entitlement));
        }

        [Theory]
        [InlineData(3, 10008)]
        [InlineData(4, 10009)]
        [InlineData(5, 10010)]
        [InlineData(6, 0)]
        public void PotionSlotProduct_MatchesCurrentEntitlement(byte current, int productId) =>
            Assert.Equal(productId, InventoryEntitlementRules.PotionSlotProduct(current));

        [Theory]
        [InlineData(3, 13, true)]
        [InlineData(3, 15, true)]
        [InlineData(3, 16, false)]
        [InlineData(6, 18, true)]
        public void PotionCell_RespectsPurchasedCount(byte slots, int cell, bool unlocked) =>
            Assert.Equal(unlocked, InventoryEntitlementRules.IsPotionCellUnlocked(cell, slots));

        [Theory]
        [InlineData(9, 0)]
        [InlineData(10, 10011)]
        [InlineData(20, 10011)]
        [InlineData(21, 10012)]
        [InlineData(40, 10012)]
        [InlineData(41, 10013)]
        public void StageRankClearProduct_MatchesLevelBands(byte level, int productId) =>
            Assert.Equal(productId, InventoryEntitlementRules.StageRankClearProduct(level));

        [Theory]
        [InlineData(0, 1440, false)]
        [InlineData(0, 1441, true)]
        [InlineData(100, 1540, false)]
        [InlineData(100, 1541, true)]
        public void StageLevelFree_RequiresMoreThanTwentyFourHours(
            long currentMarker, long nowMarker, bool expected) =>
            Assert.Equal(expected,
                InventoryEntitlementRules.CanPurchaseStageLevelFree(currentMarker, nowMarker));

        [Fact]
        public void BagSuccess_MatchesOriginalCallbackLayout() =>
            Assert.Equal(
                "32000040E2010010A400000201F82A0210040000D8040000",
                Convert.ToHexString(LobbyFrames.InventoryEntitlementAck(
                    InventoryEntitlement.Bag,
                    new EntitlementPurchaseResult(EntitlementPurchaseStatus.Success,
                        123456, 42000, 2, 11000, [1040, 1240]))));

        [Fact]
        public void CharacterSlotSuccessWithoutCoupon_HasDeterministicPadding() =>
            Assert.Equal(
                "35000040E2010010A4000005000000000000000000000000",
                Convert.ToHexString(LobbyFrames.InventoryEntitlementAck(
                    InventoryEntitlement.CharacterSlot,
                    new EntitlementPurchaseResult(EntitlementPurchaseStatus.Success,
                        123456, 42000, 5))));

        [Theory]
        [InlineData(EntitlementPurchaseStatus.Failed, "320001000000000000000000")]
        [InlineData(EntitlementPurchaseStatus.InProgress, "320002000000000000000000")]
        [InlineData(EntitlementPurchaseStatus.LimitReached, "320003000000000000000000")]
        [InlineData(EntitlementPurchaseStatus.InsufficientCash, "320004000000000000000000")]
        public void BagFailure_IsStatusOnly(
            EntitlementPurchaseStatus status, string expected) =>
            Assert.Equal(expected, Convert.ToHexString(LobbyFrames.InventoryEntitlementAck(
                InventoryEntitlement.Bag, new EntitlementPurchaseResult(status))));

        [Fact]
        public void PotionSlotSuccess_MatchesOriginalCallbackLayout() =>
            Assert.Equal(
                "6F000040E2010010A40000040210040000D8040000000000",
                Convert.ToHexString(LobbyFrames.PotionSlotPurchaseAck(
                    new PotionSlotPurchaseResult(EntitlementPurchaseStatus.Success,
                        123456, 42000, 4, [1040, 1240]))));

        [Theory]
        [InlineData(EntitlementPurchaseStatus.Failed, "6F0001000000000000000000")]
        [InlineData(EntitlementPurchaseStatus.InProgress, "6F0002000000000000000000")]
        [InlineData(EntitlementPurchaseStatus.LimitReached, "6F0003000000000000000000")]
        [InlineData(EntitlementPurchaseStatus.InsufficientCash, "6F0004000000000000000000")]
        public void PotionSlotFailure_IsStatusOnly(
            EntitlementPurchaseStatus status, string expected) =>
            Assert.Equal(expected, Convert.ToHexString(LobbyFrames.PotionSlotPurchaseAck(
                new PotionSlotPurchaseResult(status))));

        [Fact]
        public void StageRankClearSuccess_MatchesWorkerCallbackLayout() =>
            Assert.Equal(
                "70000040E2010010A4000000",
                Convert.ToHexString(LobbyFrames.StageRankClearAck(
                    new StageRankClearResult(StageEntitlementStatus.Success, 123456, 42000))));

        [Theory]
        [InlineData(StageEntitlementStatus.Failed, "700001000000000000000000")]
        [InlineData(StageEntitlementStatus.InsufficientCash, "700002000000000000000000")]
        [InlineData(StageEntitlementStatus.NotEligible, "700003000000000000000000")]
        public void StageRankClearFailure_IsStatusOnly(
            StageEntitlementStatus status, string expected) =>
            Assert.Equal(expected, Convert.ToHexString(LobbyFrames.StageRankClearAck(
                new StageRankClearResult(status))));

        [Fact]
        public void StageLevelFreeSuccess_MatchesWorkerCallbackLayout() =>
            Assert.Equal(
                "71000040E2010010A40000A1050000000000000000000000",
                Convert.ToHexString(LobbyFrames.StageLevelFreeAck(
                    new StageLevelFreeResult(
                        StageEntitlementStatus.Success, 123456, 42000, 1441))));

        [Theory]
        [InlineData(StageEntitlementStatus.Failed, "710001000000000000000000")]
        [InlineData(StageEntitlementStatus.InsufficientCash, "710002000000000000000000")]
        [InlineData(StageEntitlementStatus.NotEligible, "710003000000000000000000")]
        public void StageLevelFreeFailure_IsStatusOnly(
            StageEntitlementStatus status, string expected) =>
            Assert.Equal(expected, Convert.ToHexString(LobbyFrames.StageLevelFreeAck(
                new StageLevelFreeResult(status))));
    }
}
