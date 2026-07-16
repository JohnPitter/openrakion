using System;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        private bool _matchPowerUserSnapshot;
        private double _matchExpMultiplier = 1;
        private double _matchGoldMultiplier = 1;

        public DateTime? PuExpiresAt { get; set; }

        internal void BeginInventoryBuyPowerUser(byte[] data) => _ = HandleBuyPowerUserAsync(data);

        private async Task HandleBuyPowerUserAsync(byte[] data)
        {
            if (!PowerUserPurchaseRequest.TryParse(data, out var request))
            {
                Disconnect(0x45);
                return;
            }
            if (!TryStartInventoryMutation())
            {
                SendPowerUserFailure(2);
                return;
            }

            ShopBuyInProgress = true;
            await _storageMutationLock.WaitAsync();
            try
            {
                var result = await _server.PurchasePowerUserAsync(
                    this, request.Mode, request.UsesCoupon ? (byte)1 : (byte)0,
                    request.UsesCoupon ? request.CouponSlot : (ushort)0);
                if (result.Status == Database.PowerUserPurchaseStatus.Success)
                    SendPowerUserResponse(result);
                else
                    SendPowerUserFailure((byte)result.Status);
            }
            finally
            {
                _storageMutationLock.Release();
                ShopBuyInProgress = false;
                FinishInventoryMutation();
            }
        }

        private void SendPowerUserResponse(Database.PowerUserPurchaseResult result)
        {
            SendLobby(LobbyFrames.PowerUserPurchaseAck(result));
            Log.Ok("shop", "[{0}] 0x34 Power User confirmado: gold={1}, cash={2}, " +
                "powertime={3}, pontos={4}", Slot, result.Gold, result.Cash,
                result.PowerTimeMarker, result.PowerLevelPoints);
        }

        private void SendPowerUserFailure(byte status)
        {
            var result = new Database.PowerUserPurchaseResult(
                (Database.PowerUserPurchaseStatus)status);
            SendLobby(LobbyFrames.PowerUserPurchaseAck(result));
            Log.Warn("shop", "[{0}] 0x34 Power User recusado (status={1})", Slot, status);
        }

        internal async Task HandleStatAllocationAsync(byte statIndex)
        {
            Database.StatAllocationResult result = await _server.AllocateStatAsync(this, statIndex);
            if (result.Status != 0)
            {
                SendStatAllocationFailure(result.Status);
                return;
            }
            using var writer = new PacketWriter();
            writer.WriteWord(0x33).WriteByte(0)
                .WriteWord(result.LevelPoints)
                .WriteWord(result.PowerLevelPoints)
                .WriteByte(statIndex).WriteWord(result.Stat);
            SendLobby(writer.ToArray());
        }

        private void SendStatAllocationFailure(byte status)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x33).WriteByte(status);
            SendLobby(writer.ToArray());
        }

        internal void SnapshotPowerUserBenefits()
        {
            DateTime now = DateTime.Now;
            bool active = PuActive && (PuExpiresAt == null || PuExpiresAt > now);
            _matchExpMultiplier = active ? _server.PuConfig.EffectiveExpMult(now) : 1;
            _matchGoldMultiplier = active ? _server.PuConfig.EffectiveGoldMult(now) : 1;
            _matchPowerUserSnapshot = true;
            Log.Debug("shop", "[{0}] snapshot PU da partida: ativo={1} exp×{2} gold×{3}",
                Slot, active, _matchExpMultiplier, _matchGoldMultiplier);
        }

        internal void RefreshPowerUserExpiration(DateTime? expiresAt, DateTime now)
        {
            bool wasActive = PuActive;
            bool expirationChanged = PuExpiresAt != expiresAt;
            PuExpiresAt = expiresAt;
            PuActive = expiresAt != null && expiresAt > now;
            ExpBonusActive = PuActive;
            if (expirationChanged)
                Log.Debug("shop", "[{0}] validade PU recarregada: {1}",
                    Slot, expiresAt?.ToString("O") ?? "-");
            if (wasActive && !PuActive)
                Log.Info("shop", "[{0}] Power User expirou durante a sessão", Slot);
            else if (!wasActive && PuActive)
                Log.Info("shop", "[{0}] Power User ativado durante a sessão", Slot);
        }

        private double EffectiveExpMultiplier(DateTime now) =>
            _matchPowerUserSnapshot
                ? _matchExpMultiplier
                : ExpBonusActive ? _server.PuConfig.EffectiveExpMult(now) : 1;

        private double EffectiveGoldMultiplier(DateTime now) =>
            _matchPowerUserSnapshot
                ? _matchGoldMultiplier
                : PuActive ? _server.PuConfig.EffectiveGoldMult(now) : 1;
    }
}
