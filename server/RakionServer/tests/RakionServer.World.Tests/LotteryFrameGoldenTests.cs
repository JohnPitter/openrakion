using RakionServer.World.Database;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class LotteryFrameGoldenTests
    {
        [Fact]
        public void PurchasePrecheck_HasOriginalElevenByteLayout()
        {
            byte[] frame = LotteryFrames.PurchasePrecheck(
                LotteryPurchaseStatus.InsufficientFunds, 1000, 100);

            Assert.Equal(new byte[] {
                0x75, 0x00, 0x01,
                0xe8, 0x03, 0x00, 0x00,
                0x64, 0x00, 0x00, 0x00
            }, frame);
        }

        [Fact]
        public void PurchaseResult_HasRoundAndBothBalances()
        {
            byte[] frame = LotteryFrames.PurchaseResult(new LotteryPurchaseResult(
                LotteryPurchaseStatus.Success, 12, 9000, 450));

            Assert.Equal(new byte[] {
                0x75, 0x00, 0x00,
                0x0c, 0x00, 0x00, 0x00,
                0x28, 0x23, 0x00, 0x00,
                0xc2, 0x01, 0x00, 0x00
            }, frame);
        }

        [Fact]
        public void TicketPage_UsesNineBytesPerTicket()
        {
            byte[] frame = LotteryFrames.TicketPage(new LotteryPageResult(
                LotteryPageStatus.Success,
                [new LotteryTicket(9, new LotteryNumbers(1, 2, 3, 4, 5))]));

            Assert.Equal(new byte[] {
                0x76, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x09, 0x00, 0x00, 0x00,
                0x01, 0x02, 0x03, 0x04, 0x05
            }, frame);
        }

        [Theory]
        [InlineData(LotteryPageStatus.Empty, 1)]
        [InlineData(LotteryPageStatus.Failed, 2)]
        public void TicketPage_ErrorHasHeaderOnly(LotteryPageStatus status, byte wireStatus) =>
            Assert.Equal(new byte[] { 0x76, 0x00, wireStatus },
                LotteryFrames.TicketPage(new LotteryPageResult(status, [])));
    }
}
