using System.Threading.Tasks;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public async Task<LotteryPurchaseResult> PurchaseLotteryTicketAsync(
            ClientSession session, byte paymentType, LotteryNumbers numbers)
        {
            if (session.GameInfoId <= 0 || string.IsNullOrEmpty(session.UserId))
                return new LotteryPurchaseResult(LotteryPurchaseStatus.Rejected, 0, 0, 0);
            var gate = _characterLocks.GetOrAdd(session.GameInfoId,
                _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                return await _db.PurchaseLotteryTicketAsync(new LotteryPurchaseCommand(
                    session.GameInfoId, session.UserId, paymentType, numbers));
            }
            finally
            {
                gate.Release();
            }
        }

        public Task<LotteryPageResult> LoadLotteryTicketsAsync(
            ClientSession session, byte page) =>
            _db.LoadLotteryTicketsAsync(session.GameInfoId, page);
    }
}
