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
            SendCurrentRoomList();
            Log.Ok("lobby", "[{0}] 0x3A FieldExit -> lista de games (Status=2)", Slot);
        }

        internal void BeginFieldGameRoundStart()
        {
            var field = _server.GetField(FieldId);
            if (field == null) return;
            _server.NotifyPlayerReady(field, this);
            Log.Info("lobby", "[{0}] 0x48 cliente pronto (field={1}, seat={2})",
                Slot, field.Id, FieldSeat);
        }
    }
}
