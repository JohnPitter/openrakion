using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public bool EnterChannelLobby(ClientSession session)
        {
            Channel? channel = null;
            byte channelSlot = byte.MaxValue;
            foreach (Channel candidate in Channels)
            {
                if (session.ChannelId >= 0 && candidate.Id != session.ChannelId) continue;
                if (!candidate.TryJoin(session.Slot, out channelSlot)) continue;
                channel = candidate;
                break;
            }
            if (channel == null) return false;

            session.ChannelId = channel.Id;
            session.ChannelSlot = channelSlot;
            session.Status = UserStatus.FieldLobby;
            byte[] joined = LobbyFrames.SessionInfo(
                channelSlot, WorldSlot(session), PresenceOf(session));
            BroadcastChannel(channel, joined, UserStatus.FieldLobby);
            SendChannelSnapshot(session, 100);
            Log.Info("channel", "[{0}] entrou no canal {1}, slot local {2}",
                session.Slot, channel.Id, channelSlot);
            return true;
        }

        public void SendChannelState(ClientSession session, bool includeSelfPresence)
        {
            Channel? channel = Channels.Find(candidate => candidate.Id == session.ChannelId);
            if (channel == null || session.ChannelSlot == byte.MaxValue) return;
            if (includeSelfPresence)
                session.SendEncryptedFrame(LobbyFrames.SessionInfo(
                    session.ChannelSlot, WorldSlot(session), PresenceOf(session)));
            SendChannelSnapshot(session, 100);
        }

        public void SendChannelSnapshot(ClientSession session, int maxMembers)
        {
            Channel? channel = Channels.Find(candidate => candidate.Id == session.ChannelId);
            if (channel == null) return;
            ChannelMemberRecord[] members = SnapshotChannelMembers(channel, maxMembers);
            session.SendEncryptedFrame(LobbyFrames.ChannelList(
                channel.Type, channel.OwnerSlot, channel.Name, channel.Password, members));
        }

        public void LeaveChannel(ClientSession session, bool notify)
        {
            Channel? channel = Channels.Find(candidate => candidate.Id == session.ChannelId);
            if (channel == null || !channel.TryLeave(session.Slot, out ChannelLeaveResult leave)) return;
            session.ChannelId = -1;
            session.ChannelSlot = byte.MaxValue;
            if (notify)
            {
                BroadcastChannel(
                    channel, LobbyFrames.ChannelExit(leave.ChannelSlot), UserStatus.FieldLobby);
                if (leave.NewOwnerSlot.HasValue)
                    BroadcastChannel(
                        channel, LobbyFrames.ChannelOwner(leave.NewOwnerSlot.Value),
                        UserStatus.FieldLobby);
            }
            Log.Info("channel", "[{0}] saiu do canal {1}, slot local {2}",
                session.Slot, channel.Id, leave.ChannelSlot);
        }

        public void BroadcastChannelChat(ClientSession sender, string text)
        {
            Channel? channel = Channels.Find(candidate => candidate.Id == sender.ChannelId);
            if (channel == null || sender.ChannelSlot == byte.MaxValue) return;
            BroadcastChannel(
                channel, LobbyFrames.ChannelChat(sender.ChannelSlot, text), UserStatus.FieldLobby);
        }

        private ChannelMemberRecord[] SnapshotChannelMembers(Channel channel, int maxMembers)
        {
            ChannelMemberSlot[] slots = channel.Snapshot();
            int count = Math.Min(Math.Clamp(maxMembers, 0, 100), slots.Length);
            int start = slots.Length > count && count > 0 ? Random.Shared.Next(slots.Length) : 0;
            var result = new List<ChannelMemberRecord>(count);
            for (int index = 0; index < slots.Length && result.Count < count; index++)
            {
                ChannelMemberSlot member = slots[(start + index) % slots.Length];
                ClientSession? session = GetSession(member.SessionSlot);
                if (session == null) continue;
                result.Add(new ChannelMemberRecord(
                    member.ChannelSlot, WorldSlot(session), PresenceOf(session)));
            }
            return result.ToArray();
        }

        private void BroadcastChannel(Channel channel, byte[] frame, byte requiredStatus)
        {
            foreach (ChannelMemberSlot member in channel.Snapshot())
            {
                ClientSession? target = GetSession(member.SessionSlot);
                if (target?.Status == requiredStatus) target.SendEncryptedFrame(frame);
            }
        }

        private static ushort WorldSlot(ClientSession session) => session.Slot;

        private static ChannelPresenceRecord PresenceOf(ClientSession session) =>
            new(session.CharName, session.CharClass, session.SubStatus, session.ClanId);
    }
}
