using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public async Task<PowerUserPurchaseResult> PurchasePowerUserAsync(
            ClientSession session, byte mode, byte paymentType, ushort paymentValue)
        {
            CharacterPayment? payment = BuildCharacterPayment(
                session, paymentType, paymentValue);
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0 ||
                session.Game == null || payment == null || mode > 1)
                return new PowerUserPurchaseResult(PowerUserPurchaseStatus.Failed);

            var gate = _characterLocks.GetOrAdd(session.GameInfoId,
                _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                int price = mode == 0 ? PuConfig.Price : PuConfig.RenewalPrice;
                var command = new PowerUserPurchaseCommand(session.GameInfoId,
                    session.Game.Name, mode, price, PuConfig.BonusPoints,
                    PuConfig.DurationDays, payment);
                PowerUserPurchaseResult result = await _db.PurchasePowerUserAsync(command);
                if (result.Status != PowerUserPurchaseStatus.Success) return result;

                if (payment.UsesCoupon)
                    session.ClearBoxCell(checked((byte)payment.Slot));
                session.Gold = checked((uint)result.Gold);
                session.Cash = checked((uint)result.Cash);
                session.PowerLevelPoint = checked((uint)result.PowerLevelPoints);
                session.PowerTimeMarker = result.PowerTimeMarker;
                session.PuExpiresAt = result.ExpiresAt;
                session.PuActive = true;
                session.ExpBonusActive = true;
                session.LoginCharList = await BuildLoginCharListAsync(session);
                Log.Ok("shop", "[{0}] comprou Power User modo={1}; cash={2} pontos={3} expira={4}",
                    session.Slot, mode, result.Cash, result.PowerLevelPoints,
                    result.ExpiresAt?.ToString("O") ?? "-");
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<StatAllocationResult> AllocateStatAsync(
            ClientSession session, byte statIndex)
        {
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0)
                return new StatAllocationResult(1);
            var gate = _characterLocks.GetOrAdd(session.GameInfoId,
                _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                StatAllocationResult result = await _db.AllocateStatTransactionalAsync(
                    session.GameInfoId, session.ActiveCharId, statIndex);
                if (result.Status != 0) return result;
                session.Stats[statIndex] = result.Stat;
                session.CharLevelPoint = result.LevelPoints;
                session.PowerLevelPoint = result.PowerLevelPoints;
                Log.Ok("shop", "[{0}] stat {1} -> {2}; levelPoints={3}, puPoints={4}",
                    session.Slot, statIndex, result.Stat,
                    result.LevelPoints, result.PowerLevelPoints);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
