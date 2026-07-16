using System;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class ChannelLobbyTests
    {
        [Fact]
        public void SessionInfo_SerializesFullPresenceRecord()
        {
            byte[] frame = LobbyFrames.SessionInfo(
                7, 0x1234, new ChannelPresenceRecord("Alice", 4, 2, 0x11223344));

            Assert.Equal(
                "1F0000073412416C69636500040244332211000000000000",
                Convert.ToHexString(frame));
        }

        [Fact]
        public void ChannelList_SerializesMultipleMembersAndLocalSlots()
        {
            byte[] frame = LobbyFrames.ChannelList(2, 5, "ch", "pw", new[]
            {
                new ChannelMemberRecord(0, 1, new ChannelPresenceRecord("A", 0, 0, 0)),
                new ChannelMemberRecord(5, 2, new ChannelPresenceRecord("B", 4, 1, 9))
            });

            Assert.Equal(
                "1E0002020563680070770000010041000000000000000502004200040109000000000000",
                Convert.ToHexString(frame));
        }

        [Fact]
        public void ChannelExitChatAndOwner_UseOriginalLocalSlotLayout()
        {
            Assert.Equal("200007000000000000000000", Convert.ToHexString(LobbyFrames.ChannelExit(7)));
            Assert.Equal("22000368656C6C6F00000000", Convert.ToHexString(LobbyFrames.ChannelChat(3, "hello")));
            Assert.Equal("280001000000000000000000", Convert.ToHexString(LobbyFrames.ChannelOwner(1)));
        }

        [Fact]
        public void Channel_AssignsStableSlotsAndReusesReleasedSlot()
        {
            var channel = new Channel(1, new ChannelOptions { Capacity = 2, ManagedOwner = true });

            Assert.True(channel.TryJoin(10, out byte first));
            Assert.True(channel.TryJoin(11, out byte second));
            Assert.True(channel.TryJoin(10, out byte repeated));
            Assert.False(channel.TryJoin(12, out _));
            Assert.Equal((byte)0, first);
            Assert.Equal((byte)1, second);
            Assert.Equal(first, repeated);

            Assert.True(channel.TryLeave(10, out ChannelLeaveResult leave));
            Assert.True(channel.TryJoin(12, out byte reused));
            Assert.Equal(leave.ChannelSlot, reused);
            Assert.Equal(second, leave.NewOwnerSlot);
        }

        [Fact]
        public void Channel_TransfersOwnerOnlyWhenCurrentOwnerLeaves()
        {
            var channel = new Channel(1, new ChannelOptions { Capacity = 3, ManagedOwner = true });
            channel.TryJoin(10, out byte owner);
            channel.TryJoin(11, out byte second);
            channel.TryJoin(12, out byte third);

            Assert.True(channel.TryLeave(11, out ChannelLeaveResult nonOwnerLeave));
            Assert.Null(nonOwnerLeave.NewOwnerSlot);

            Assert.True(channel.TryLeave(10, out ChannelLeaveResult ownerLeave));
            Assert.Equal(owner, ownerLeave.ChannelSlot);
            Assert.Equal(third, ownerLeave.NewOwnerSlot);

            Assert.True(channel.TryLeave(12, out ChannelLeaveResult finalLeave));
            Assert.Null(finalLeave.NewOwnerSlot);
        }

        [Fact]
        public void DefaultChannel_KeepsOriginalOwnerSentinelAndDoesNotTransfer()
        {
            var channel = new Channel(0, new ChannelOptions { Name = "channel01" });
            channel.TryJoin(10, out _);
            channel.TryJoin(11, out _);

            Assert.Equal(Channel.NoOwnerSlot, channel.OwnerSlot);
            Assert.True(channel.TryLeave(10, out ChannelLeaveResult leave));
            Assert.Null(leave.NewOwnerSlot);
            Assert.Equal(Channel.NoOwnerSlot, channel.OwnerSlot);
        }

        [Fact]
        public void Channel_UsesAllOneHundredWireSlotsAndRejectsOverflow()
        {
            var channel = new Channel(0);

            for (ushort sessionSlot = 0; sessionSlot < 100; sessionSlot++)
            {
                Assert.True(channel.TryJoin(sessionSlot, out byte channelSlot));
                Assert.Equal((byte)sessionSlot, channelSlot);
            }

            Assert.False(channel.TryJoin(100, out byte rejectedSlot));
            Assert.Equal(byte.MaxValue, rejectedSlot);
            Assert.Equal(100, channel.Snapshot().Length);
        }
    }
}
