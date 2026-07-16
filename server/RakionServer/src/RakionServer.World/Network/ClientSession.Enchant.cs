using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        private PendingEnchant? _pendingEnchant;
        private byte[]? _lastEnchantCommit;
        private PendingEnchant? _lastCommittedEnchant;
        private DateTime _lastEnchantCommitAt;

        internal async Task HandleEnchantPreviewAsync(
            byte target, byte catalyst, IReadOnlyList<byte> materialSlots)
        {
            await _storageMutationLock.WaitAsync();
            try
            {
                byte slotStatus = EnchantRules.ValidateSlots(target, catalyst, materialSlots);
                if (slotStatus != 0)
                {
                    SendEnchantStatus(slotStatus);
                    return;
                }
                EnchantSelection? selection = SnapshotEnchantSelection(
                    target, catalyst, materialSlots);
                if (selection == null)
                {
                    SendEnchantStatus(7);
                    return;
                }
                var prepared = await _server.PrepareEnchantAsync(this, selection);
                if (prepared.Pending == null)
                {
                    SendEnchantStatus(prepared.Status);
                    return;
                }
                _pendingEnchant = prepared.Pending;
                SendEnchantPreview(prepared.Pending);
                Log.Debug("enchant", "[{0}] preview op={1} alvo={2} cat={3} mats={4}",
                    Slot, prepared.Pending.OperationId, target, catalyst, materialSlots.Count);
            }
            finally
            {
                _storageMutationLock.Release();
            }
        }

        private async Task HandleEnchantCommitAsync(byte[] data)
        {
            if (data.Length < 8 || data[3] >= 4)
            {
                Disconnect(0xe4);
                return;
            }
            if (TryReplayEnchantCommit(data)) return;
            PendingEnchant? pending = _pendingEnchant;
            if (pending == null || data[0] != 0 || !CommitMatches(data, pending.Selection))
            {
                _pendingEnchant = null;
                SendEnchantResult(9, data[1], data[2], data[3], data.AsSpan(4, 3));
                return;
            }

            EnchantCommitResult result = await _server.CommitEnchantAsync(this, pending);
            if (!result.Success)
            {
                SendEnchantResult(9, pending.Selection);
                return;
            }
            _pendingEnchant = null;
            _lastEnchantCommit = (byte[])data.Clone();
            _lastCommittedEnchant = pending;
            _lastEnchantCommitAt = DateTime.UtcNow;
            SendEnchantResult(pending.Result, pending.Selection);
        }

        public async Task ReplaceStorageAfterEnchantAsync(IReadOnlyList<StorageItem> storage)
        {
            await _storageMutationLock.WaitAsync();
            try
            {
                SetBoxItems(storage);
            }
            finally
            {
                _storageMutationLock.Release();
            }
        }

        private EnchantSelection? SnapshotEnchantSelection(
            byte target, byte catalyst, IReadOnlyList<byte> materials)
        {
            EnchantItemRef? targetItem = BoxItemRef(target);
            EnchantItemRef? catalystItem = BoxItemRef(catalyst);
            if (targetItem == null || catalystItem == null) return null;
            var materialItems = new List<EnchantItemRef>(materials.Count);
            foreach (byte slot in materials)
            {
                EnchantItemRef? item = BoxItemRef(slot);
                if (item == null) return null;
                materialItems.Add(item);
            }
            return new EnchantSelection(GameInfoId, targetItem,
                BoxLevel[target], catalystItem, materialItems);
        }

        private EnchantItemRef? BoxItemRef(byte slot)
        {
            if (slot >= BoxItems.Count || BoxItems[slot] == 0 || BoxRowId[slot] <= 0)
                return null;
            return new EnchantItemRef(slot, BoxRowId[slot], BoxItems[slot]);
        }

        private void SendEnchantPreview(PendingEnchant pending)
            => SendLobby(EnchantFrames.Preview(
                pending, (uint)GameInfoId, (uint)ActiveCharId));

        private static bool CommitMatches(byte[] data, EnchantSelection selection)
        {
            if (data[1] != selection.Target.Slot || data[2] != selection.Catalyst.Slot ||
                data[3] != selection.Materials.Count)
                return false;
            for (int i = 0; i < selection.Materials.Count; i++)
                if (data[4 + i] != selection.Materials[i].Slot) return false;
            return true;
        }

        private bool TryReplayEnchantCommit(byte[] data)
        {
            if (_lastEnchantCommit == null || _lastCommittedEnchant == null ||
                DateTime.UtcNow - _lastEnchantCommitAt > TimeSpan.FromSeconds(30) ||
                !_lastEnchantCommit.SequenceEqual(data))
                return false;
            SendEnchantResult(_lastCommittedEnchant.Result, _lastCommittedEnchant.Selection);
            return true;
        }

        private void SendEnchantStatus(byte status)
            => SendLobby(EnchantFrames.Status(status));

        private void SendEnchantResult(byte result, EnchantSelection selection)
        {
            Span<byte> materials = stackalloc byte[selection.Materials.Count];
            for (int i = 0; i < selection.Materials.Count; i++)
                materials[i] = selection.Materials[i].Slot;
            SendLobby(EnchantFrames.Result(result, selection.Target.Slot,
                selection.Catalyst.Slot, materials));
        }

        private void SendEnchantResult(
            byte result, byte target, byte catalyst, byte count, ReadOnlySpan<byte> materials)
        {
            SendLobby(EnchantFrames.Result(result, target, catalyst, materials[..count]));
        }
    }
}
