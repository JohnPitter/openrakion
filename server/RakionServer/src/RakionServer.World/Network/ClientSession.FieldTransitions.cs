using RakionServer.Common;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void BeginFieldExit()
        {
            _server.LeaveField(this);
            InField = true;
            FieldSecondary = true;
            SecondActive = true;
            Status = 2;
            _server.SendChannelState(this, includeSelfPresence: true);
            SendEncryptedFrame(LobbyFrames.GameList(_server.ListJoinableFields(0, 10)));
            Log.Ok("lobby", "[{0}] 0x3A FieldExit -> lista de games (Status=2)", Slot);
        }

        internal void BeginFieldGameRoundStart()
        {
            int durationSeconds = PendingRoomMode == 0
                ? _server.StageDurationSeconds(PendingRoomMap) ?? 432
                : PendingRoomDurationSec > 0 ? PendingRoomDurationSec : 432;
            SendEncryptedFrame(LobbyFrames.RemainingTime(durationSeconds));
            Log.Info("lobby", "[{0}] 0x48 RoundStart (RemainingSec={1}s)",
                Slot, durationSeconds + 3);
        }
    }
}
