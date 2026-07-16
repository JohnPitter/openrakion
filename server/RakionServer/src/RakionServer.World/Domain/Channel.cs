using System;
using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Canal social do World. O original mantém até 100 entradas de oito bytes:
    /// slot global da sessão, flag de ocupação e um slot local estável de 0 a 99.
    /// </summary>
    public sealed class Channel
    {
        public const byte NoOwnerSlot = 100;
        private const int WireSlotCount = NoOwnerSlot;
        private readonly ushort?[] _members = new ushort?[WireSlotCount];
        private readonly bool _managedOwner;
        private byte _ownerSlot = NoOwnerSlot;

        public int Id { get; }
        public string Name { get; }
        public string Password { get; }
        public byte Type { get; }
        public byte Capacity { get; }
        public bool Special { get; }
        public byte OwnerSlot
        {
            get { lock (_members) return _ownerSlot; }
        }

        public Channel(int id, ChannelOptions? options = null)
        {
            options ??= new ChannelOptions();
            Id = id;
            Name = options.Name.Length > 0 ? options.Name : $"Channel{id}";
            Password = options.Password;
            Type = options.Type;
            Capacity = (byte)Math.Clamp((int)options.Capacity, 1, WireSlotCount);
            Special = options.Special;
            _managedOwner = options.ManagedOwner;
        }

        public bool TryJoin(ushort sessionSlot, out byte channelSlot)
        {
            lock (_members)
            {
                for (byte slot = 0; slot < WireSlotCount; slot++)
                {
                    if (_members[slot] != sessionSlot) continue;
                    channelSlot = slot;
                    return true;
                }
                for (byte slot = 0; slot < Capacity; slot++)
                {
                    if (_members[slot].HasValue) continue;
                    _members[slot] = sessionSlot;
                    if (_managedOwner && _ownerSlot == NoOwnerSlot) _ownerSlot = slot;
                    channelSlot = slot;
                    return true;
                }
            }
            channelSlot = byte.MaxValue;
            return false;
        }

        public bool TryLeave(ushort sessionSlot, out ChannelLeaveResult result)
        {
            lock (_members)
            {
                for (byte slot = 0; slot < WireSlotCount; slot++)
                {
                    if (_members[slot] != sessionSlot) continue;
                    _members[slot] = null;
                    byte? newOwnerSlot = _managedOwner && slot == _ownerSlot
                        ? TransferOwner()
                        : null;
                    result = new ChannelLeaveResult(slot, newOwnerSlot);
                    return true;
                }
            }
            result = default;
            return false;
        }

        private byte? TransferOwner()
        {
            for (byte slot = 0; slot < WireSlotCount; slot++)
            {
                if (!_members[slot].HasValue) continue;
                _ownerSlot = slot;
                return slot;
            }

            _ownerSlot = NoOwnerSlot;
            return null;
        }

        public ChannelMemberSlot[] Snapshot()
        {
            var result = new List<ChannelMemberSlot>();
            lock (_members)
            {
                for (byte slot = 0; slot < WireSlotCount; slot++)
                    if (_members[slot].HasValue)
                        result.Add(new ChannelMemberSlot(slot, _members[slot]!.Value));
            }
            return result.ToArray();
        }
    }

    public readonly record struct ChannelMemberSlot(byte ChannelSlot, ushort SessionSlot);
    public readonly record struct ChannelLeaveResult(byte ChannelSlot, byte? NewOwnerSlot);

    public sealed record ChannelOptions
    {
        public string Name { get; init; } = "";
        public string Password { get; init; } = "";
        public byte Type { get; init; }
        public byte Capacity { get; init; } = 100;
        public bool Special { get; init; }
        public bool ManagedOwner { get; init; }
    }

    /// <summary>Grupo/IDC externo validado pelo opcode 0x01 (this+0x60/0x64/0x68).</summary>
    public readonly record struct WorldGroup(int Id, bool Special);
}
