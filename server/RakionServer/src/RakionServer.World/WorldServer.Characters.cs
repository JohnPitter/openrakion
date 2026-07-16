using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public async Task<CharacterCreateResult> CreateCharacterAsync(
            ClientSession session, string name, byte charClass, byte slot)
        {
            if (!Domain.CharacterLifecycleRules.CanCreate(
                    session.GameInfoId, session.ActiveCharId, charClass, slot) ||
                !Domain.LegacyIdentity.IsValidCharacterName(name))
                return new CharacterCreateResult(CharacterCreateStatus.Failed);

            await _characterNameLock.WaitAsync();
            var accountLock = _characterLocks.GetOrAdd(session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await accountLock.WaitAsync();
            try
            {
                var result = await _db.CreateCharacterAsync(session.GameInfoId, name, charClass, slot);
                if (result.Status != CharacterCreateStatus.Success) return result;
                session.LoginCharList = await BuildLoginCharListAsync(session);
                Log.Ok("character", "[{0}] criou char '{1}' id={2} class={3} slot={4}",
                    session.Slot, name, result.Character!.Id, charClass, slot);
                return result;
            }
            finally
            {
                accountLock.Release();
                _characterNameLock.Release();
            }
        }

        public async Task<CharacterStateClearResult> ClearCharacterStateAsync(
            ClientSession session, byte paymentType, ushort paymentValue)
        {
            var payment = BuildCharacterPayment(session, paymentType, paymentValue);
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0 || payment == null)
                return new CharacterStateClearResult(CharacterStateClearStatus.Failed);
            var gate = _characterLocks.GetOrAdd(session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var result = await _db.ClearCharacterStateAsync(
                    session.GameInfoId, session.UserId, session.ActiveCharId, payment);
                if (result.Status != CharacterStateClearStatus.Success) return result;
                if (payment.UsesCoupon) session.ClearBoxCell(checked((byte)payment.Slot));
                session.Cash = checked((uint)result.Cash);
                session.CharLevelPoint = result.LevelPoint;
                session.PowerLevelPoint = result.PowerLevelPoint;
                System.Array.Clear(session.Stats);
                session.LoginCharList = await BuildLoginCharListAsync(session);
                NotifyRandomPresents(session, result.Presents);
                Log.Ok("character", "[{0}] resetou stats do char={1}; cash={2} lp={3} power={4} pagamento={5}",
                    session.Slot, session.ActiveCharId, result.Cash, result.LevelPoint,
                    result.PowerLevelPoint, payment.UsesCoupon ? "coupon" : "cash");
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<CharacterRenameResult> RenameCharacterAsync(
            ClientSession session, string newName, byte paymentType, ushort paymentValue)
        {
            var payment = BuildCharacterPayment(session, paymentType, paymentValue);
            if (session.GameInfoId <= 0 || session.ActiveCharId <= 0 || payment == null ||
                !Domain.LegacyIdentity.IsValidCharacterName(newName))
                return new CharacterRenameResult(CharacterRenameStatus.Failed);
            await _characterNameLock.WaitAsync();
            var gate = _characterLocks.GetOrAdd(session.GameInfoId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var result = await _db.RenameCharacterAsync(
                    session.GameInfoId, session.UserId, session.ActiveCharId, newName, payment);
                if (result.Status != CharacterRenameStatus.Success) return result;
                if (payment.UsesCoupon) session.ClearBoxCell(checked((byte)payment.Slot));
                session.Cash = checked((uint)result.Cash);
                session.CharName = newName;
                session.LoginCharList = await BuildLoginCharListAsync(session);
                NotifyRandomPresents(session, result.Presents);
                Log.Ok("character", "[{0}] renomeou char={1} para '{2}'; cash={3} pagamento={4}",
                    session.Slot, session.ActiveCharId, newName, result.Cash,
                    payment.UsesCoupon ? "coupon" : "cash");
                return result;
            }
            finally
            {
                gate.Release();
                _characterNameLock.Release();
            }
        }

        private static CharacterPayment? BuildCharacterPayment(
            ClientSession session, byte paymentType, ushort paymentValue)
        {
            if (paymentType == 0) return paymentValue == 0 ? new CharacterPayment(0) : null;
            if (paymentType != 1) return null;
            if (paymentValue >= session.BoxItems.Count) return new CharacterPayment(1, paymentValue);
            int slot = paymentValue;
            return new CharacterPayment(1, paymentValue, session.BoxRowId[slot], session.BoxItems[slot]);
        }
    }
}
