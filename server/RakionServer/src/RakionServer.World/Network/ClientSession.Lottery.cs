using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal async Task HandleLotteryPurchaseAsync(byte[] data)
        {
            if (data.Length < 6) { Disconnect(0xe7); return; }
            byte paymentType = data[0];
            if (!LotteryRules.IsPaymentType(paymentType)) { Disconnect(0xe7); return; }
            var numbers = new LotteryNumbers(data[1], data[2], data[3], data[4], data[5]);
            if (!HasLotteryFunds(paymentType))
            {
                SendLobby(LotteryFrames.PurchasePrecheck(
                    LotteryPurchaseStatus.InsufficientFunds, Gold, Cash));
                return;
            }
            if (LotteryRules.HasRepeatedNumber(numbers.ToArray()))
            {
                SendLobby(LotteryFrames.PurchasePrecheck(
                    LotteryPurchaseStatus.Rejected, Gold, Cash));
                return;
            }
            if (!await _lotteryPurchaseLock.WaitAsync(0))
            {
                SendLobby(LotteryFrames.PurchasePrecheck(
                    LotteryPurchaseStatus.Rejected, Gold, Cash));
                return;
            }
            try
            {
                LotteryPurchaseResult result = await _server.PurchaseLotteryTicketAsync(
                    this, paymentType, numbers);
                if (result.Status == LotteryPurchaseStatus.Success)
                {
                    Gold = checked((uint)result.Gold);
                    Cash = checked((uint)result.Cash);
                }
                SendLobby(LotteryFrames.PurchaseResult(result));
            }
            finally
            {
                _lotteryPurchaseLock.Release();
            }
        }

        internal async Task HandleLotteryPageAsync(byte[] data)
        {
            if (data.Length < 1) { Disconnect(0xe9); return; }
            LotteryPageResult result = await _server.LoadLotteryTicketsAsync(this, data[0]);
            SendLobby(LotteryFrames.TicketPage(result));
        }

        private bool HasLotteryFunds(byte paymentType) => paymentType == 0
            ? Gold >= LotteryRules.GoldCost
            : Cash >= LotteryRules.CashCost;
    }
}
