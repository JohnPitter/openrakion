using System;
using System.Threading.Tasks;
using RakionServer.World.Database;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void BeginPresentPeek() => _ = HandlePresentPeekAsync();

        internal void BeginPresentAccept(byte[] data) => _ = HandlePresentAcceptAsync(data);

        internal void BeginPresentDispose(byte[] data) => _ = HandlePresentDisposeAsync(data);

        public async Task<PresentAcceptResult> AcceptPresentIntoStorageAsync(
            ushort slot, Func<bool, Task<PresentAcceptResult>> persist)
        {
            await _storageMutationLock.WaitAsync();
            try
            {
                bool available = slot < BoxItems.Count && BoxItems[slot] == 0;
                PresentAcceptResult result = await persist(available);
                if (result.Status == PresentAcceptStatus.Success)
                    SetBoxCell(slot, result.ItemId, result.Level, result.StorageRowId);
                return result;
            }
            finally
            {
                _storageMutationLock.Release();
            }
        }

        private async Task HandlePresentPeekAsync()
        {
            PresentPeekResult result = await _server.PeekPresentAsync(this);
            SendEncryptedFrame(LobbyFrames.PresentPeekAck(result));
        }

        private async Task HandlePresentAcceptAsync(byte[] data)
        {
            if (!PresentAcceptRequest.TryParse(data, out var request))
            {
                Disconnect(0xc3);
                return;
            }
            PresentAcceptResult result = await _server.AcceptPresentAsync(
                this, request.PendingId, request.Slot);
            if (result.Status is PresentAcceptStatus.NotFirst or PresentAcceptStatus.Empty)
            {
                Disconnect(0xc5);
                return;
            }
            SendEncryptedFrame(LobbyFrames.PresentAcceptAck(result));
        }

        private async Task HandlePresentDisposeAsync(byte[] data)
        {
            if (!PresentDisposeRequest.TryParse(data, out var request))
            {
                Disconnect(0xc4);
                return;
            }
            PresentDisposeResult result = await _server.DisposePresentAsync(this, request.PendingId);
            if (result.Status is PresentDisposeStatus.NotFirst or PresentDisposeStatus.Empty)
            {
                Disconnect(0xc6);
                return;
            }
            SendEncryptedFrame(LobbyFrames.PresentDisposeAck(result));
        }

    }
}
