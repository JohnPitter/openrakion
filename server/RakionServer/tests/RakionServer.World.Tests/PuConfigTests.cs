using System;
using RakionServer.World.Database;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public class PuConfigTests
    {
        [Fact]
        public void DefaultsMatchOriginalPowerUserPurchase()
        {
            var config = new PuConfig();

            Assert.Equal(8000, config.Price);
            Assert.Equal(6000, config.RenewalPrice);
            Assert.Equal(5, config.BonusPoints);
            Assert.Equal(30, config.DurationDays);
            Assert.Equal(1.5, config.ExpMult);
            Assert.Equal(1.0, config.GoldMult);
            Assert.Equal(1.0, config.PromoGoldMult);
        }

        [Fact]
        public void SuccessFrameMatchesWorldAndClientCallbacks()
        {
            var result = new PowerUserPurchaseResult(PowerUserPurchaseStatus.Success,
                Gold: 100, Cash: 200, PowerTimeMarker: 0x12345678,
                PowerLevelPoints: 5);

            Assert.Equal("34000064000000C800000078563412050000",
                Convert.ToHexString(LobbyFrames.PowerUserPurchaseAck(result)));
        }

        [Fact]
        public void FailureFrameHasOnlyStatus()
        {
            var result = new PowerUserPurchaseResult(
                PowerUserPurchaseStatus.InsufficientCash);

            Assert.Equal("340003",
                Convert.ToHexString(LobbyFrames.PowerUserPurchaseAck(result)));
        }

        [Fact]
        public void NormalMultipliersApplyOutsidePromotionWindow()
        {
            var config = new PuConfig
            {
                ExpMult = 1.5,
                GoldMult = 1.4,
                PromoActive = true,
                PromoStart = new DateTime(2026, 7, 20),
                PromoEnd = new DateTime(2026, 7, 25)
            };

            Assert.Equal(1.5, config.EffectiveExpMult(new DateTime(2026, 7, 15)));
            Assert.Equal(1.4, config.EffectiveGoldMult(new DateTime(2026, 7, 15)));
        }

        [Fact]
        public void PromotionMultipliersApplyInsideInclusiveWindow()
        {
            var start = new DateTime(2026, 7, 20);
            var config = new PuConfig
            {
                PromoActive = true,
                PromoExpMult = 2,
                PromoGoldMult = 2.5,
                PromoStart = start,
                PromoEnd = start.AddDays(1)
            };

            Assert.Equal(2, config.EffectiveExpMult(start));
            Assert.Equal(2.5, config.EffectiveGoldMult(start.AddDays(1)));
        }
    }
}
