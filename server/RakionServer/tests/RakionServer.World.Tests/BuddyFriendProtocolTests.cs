using System;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using RakionServer.Buddy;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BuddyFriendProtocolTests
{
    [Fact]
    public void LoginCarriesTokenCountAndExact148ByteRecords()
    {
        byte[] extension = new byte[32];
        extension[0] = 0xAA;
        byte[] payload = BuddyFriendCodec.BuildLogin(0x11223344, [
            new BuddyFriendRecord("friend", "Amigo", "Grupo", extension)]);

        Assert.Equal(156, payload.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt16LittleEndian(payload));
        Assert.Equal(0x11223344u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6)));
        Assert.Equal("friend", ReadAnsi(payload.AsSpan(8, 20)));
        Assert.Equal("Amigo", ReadWide(payload.AsSpan(28, 40)));
        Assert.Equal("Grupo", ReadWide(payload.AsSpan(68, 40)));
        Assert.Equal(0xAA, payload[124]);
    }

    [Fact]
    public void AddAndRemoveResponsesIncludeClientConsumedRecords()
    {
        var friend = new BuddyFriendRecord("friend", "Amigo", "", new byte[32]);

        byte[] added = BuddyFriendCodec.BuildAddResult(0, friend);
        byte[] removed = BuddyFriendCodec.BuildRemoveResult(0, "friend");

        Assert.Equal(150, added.Length);
        Assert.Equal("friend", ReadAnsi(added.AsSpan(2, 20)));
        Assert.Equal(22, removed.Length);
        Assert.Equal("friend", ReadAnsi(removed.AsSpan(2, 20)));
        Assert.Equal(2, BuddyFriendCodec.BuildAddResult(1, null).Length);
    }

    [Fact]
    public void ParsesAllRecoveredFixedServiceLayouts()
    {
        byte[] add = new byte[52];
        Encoding.Latin1.GetBytes("friend").CopyTo(add, 0);
        add[20] = 0xA1;
        Assert.True(BuddyFriendCodec.TryParseAdd(add, out string account, out byte[] ext));
        Assert.Equal("friend", account);
        Assert.Equal(0xA1, ext[0]);

        byte[] wide = new byte[40];
        Encoding.Unicode.GetBytes("Guild").CopyTo(wide, 0);
        Assert.True(BuddyFriendCodec.TryParseWideName(wide, out string guild));
        Assert.Equal("Guild", guild);
        Assert.True(BuddyFriendCodec.TryParseExtUser(new byte[16], out _));
        Assert.False(BuddyFriendCodec.TryParseExtUser(new byte[15], out _));
    }

    [Fact]
    public void GroupLayoutsMatchClientBuilders()
    {
        byte[] add = new byte[44];
        BinaryPrimitives.WriteUInt16LittleEndian(add, 7);
        Encoding.Unicode.GetBytes("Raid").CopyTo(add, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(add.AsSpan(42), 9);
        Assert.True(BuddyFriendCodec.TryParseGroupAdd(add, out BuddyGroupRecord group));
        Assert.Equal(new BuddyGroupRecord(7, "Raid", 9), group);

        byte[] list = BuddyFriendCodec.BuildGroupList(0, [group]);
        Assert.Equal(48, list.Length);
        Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(list.AsSpan(4)));
        Assert.Equal("Raid", ReadWide(list.AsSpan(6, 40)));

        byte[] members = new byte[62];
        BinaryPrimitives.WriteUInt16LittleEndian(members, 1);
        Encoding.Latin1.GetBytes("friend").CopyTo(members, 2);
        Encoding.Unicode.GetBytes("Raid").CopyTo(members, 22);
        Assert.True(BuddyFriendCodec.TryParseGroupMembers(
            members, out string[] ids, out string name));
        Assert.Equal(["friend"], ids);
        Assert.Equal("Raid", name);
    }

    [Fact]
    public void PresenceUsesCountVariableRecordsAndNetworkEndpointBytes()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 8500);

        byte[] online = BuddyPresenceCodec.BuildState("friend", endpoint);
        byte[] offline = BuddyPresenceCodec.BuildState("friend", null);
        byte[] vip = BuddyPresenceCodec.BuildVipEndpoint(endpoint);

        Assert.Equal(35, online.Length);
        Assert.Equal(23, offline.Length);
        Assert.Equal("0100667269656E64000000000000000000000000000001CB0071072134CB0071072134",
            Convert.ToHexString(online));
        Assert.Equal("CB0071072134", Convert.ToHexString(vip));
    }

    [Fact]
    public void TunnelRequestMatchesRecoveredVariableBuilder()
    {
        byte[] payload = new byte[50];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), 0xC011);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(3), 3);
        Encoding.Latin1.GetBytes("abc").CopyTo(payload, 5);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), 2);
        Encoding.Latin1.GetBytes("bob").CopyTo(payload, 10);
        Encoding.Latin1.GetBytes("alice").CopyTo(payload, 30);

        Assert.True(BuddyTunnelCodec.TryParseRequest(payload, out BuddyTunnelRequest request));
        Assert.Equal(1, request.Flags);
        Assert.Equal(0xC011, request.InnerOpcode);
        Assert.Equal("abc", Encoding.Latin1.GetString(request.InnerPayload));
        Assert.Equal(["bob", "alice"], request.Recipients);
        Assert.False(BuddyTunnelCodec.TryParseRequest(payload[..^1], out _));
    }

    [Fact]
    public void TunnelNotificationMatchesRecoveredConsumerOffsets()
    {
        byte[] payload = BuddyTunnelCodec.BuildNotification(
            new BuddyFriendRecord("alice", "Alice", "Raid", new byte[32]),
            0xC011, "hi"u8);

        Assert.Equal(106, payload.Length);
        Assert.Equal("alice", ReadAnsi(payload.AsSpan(0, 20)));
        Assert.Equal("Alice", ReadWide(payload.AsSpan(20, 40)));
        Assert.Equal("Raid", ReadWide(payload.AsSpan(60, 40)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(100)));
        Assert.Equal(0xC011, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(102)));
        Assert.Equal("hi", Encoding.Latin1.GetString(payload.AsSpan(104)));
    }

    [Theory]
    [InlineData(0xC011, false, false)]
    [InlineData(0xC011, true, true)]
    [InlineData(0xC041, false, true)]
    [InlineData(0xC042, false, true)]
    public void TunnelPolicyOnlyOpensFriendHandshakeForUnrelatedUsers(
        ushort opcode, bool related, bool expected)
    {
        Assert.Equal(expected, BuddyTunnelPolicy.CanRelay(opcode, related));
    }

    private static string ReadAnsi(ReadOnlySpan<byte> source)
    {
        int end = source.IndexOf((byte)0);
        return Encoding.Latin1.GetString(source[..end]);
    }

    private static string ReadWide(ReadOnlySpan<byte> source)
    {
        int end = 0;
        while (end + 1 < source.Length && (source[end] != 0 || source[end + 1] != 0)) end += 2;
        return Encoding.Unicode.GetString(source[..end]);
    }
}
