using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace RakionServer.Buddy;

public sealed record BuddyFriendRecord(
    string AccountId, string DisplayName, string GroupName, byte[] Extension);

public sealed record BuddyGroupRecord(ushort Id, string Name, ushort Flags);

public static class BuddyFriendCodec
{
    public const int AccountIdLength = 20;
    public const int WideNameLength = 40;
    public const int ExtensionLength = 32;
    public const int FriendRecordLength = 148;
    public const int GroupRecordLength = 44;

    public static byte[] BuildLogin(
        uint udpToken, IReadOnlyList<BuddyFriendRecord> friends)
    {
        int count = Math.Min(500, friends.Count);
        byte[] payload = new byte[8 + count * FriendRecordLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2), udpToken);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)count);
        for (int i = 0; i < count; i++)
            WriteFriend(payload.AsSpan(8 + i * FriendRecordLength), friends[i]);
        return payload;
    }

    public static byte[] BuildAddResult(ushort result, BuddyFriendRecord? friend)
    {
        byte[] payload = new byte[result == 0 && friend != null ? 150 : 2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, result);
        if (friend != null && result == 0) WriteFriend(payload.AsSpan(2), friend);
        return payload;
    }

    public static byte[] BuildRemoveResult(ushort result, string accountId)
    {
        byte[] payload = new byte[result == 0 ? 22 : 2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, result);
        if (result == 0) WriteAnsi(payload.AsSpan(2), accountId);
        return payload;
    }

    public static byte[] BuildGroupList(
        ushort result, IReadOnlyList<BuddyGroupRecord> groups)
    {
        int count = result == 0 ? Math.Min(50, groups.Count) : 0;
        byte[] payload = new byte[4 + count * GroupRecordLength];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, result);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)count);
        for (int i = 0; i < count; i++)
        {
            Span<byte> record = payload.AsSpan(4 + i * GroupRecordLength, GroupRecordLength);
            BinaryPrimitives.WriteUInt16LittleEndian(record, groups[i].Id);
            WriteWide(record.Slice(2, WideNameLength), groups[i].Name);
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(42), groups[i].Flags);
        }
        return payload;
    }

    public static bool TryParseAdd(
        ReadOnlySpan<byte> payload, out string accountId, out byte[] extension)
    {
        accountId = "";
        extension = Array.Empty<byte>();
        if (payload.Length != AccountIdLength + ExtensionLength ||
            !TryReadAnsi(payload[..AccountIdLength], out accountId)) return false;
        extension = payload[AccountIdLength..].ToArray();
        return true;
    }

    public static bool TryParseAccount(ReadOnlySpan<byte> payload, out string accountId)
    {
        accountId = "";
        return payload.Length == AccountIdLength && TryReadAnsi(payload, out accountId);
    }

    public static bool TryParseWideName(ReadOnlySpan<byte> payload, out string value)
    {
        value = "";
        return payload.Length == WideNameLength && TryReadWide(payload, out value);
    }

    public static bool TryParseExtUser(ReadOnlySpan<byte> payload, out byte[] extension)
    {
        extension = payload.Length == 16 ? payload.ToArray() : Array.Empty<byte>();
        return extension.Length != 0;
    }

    public static bool TryParseExtList(
        ReadOnlySpan<byte> payload, out string accountId, out byte[] extension) =>
        TryParseAdd(payload, out accountId, out extension);

    public static bool TryParseGroupAdd(
        ReadOnlySpan<byte> payload, out BuddyGroupRecord group)
    {
        group = new BuddyGroupRecord(0, "", 0);
        if (payload.Length != GroupRecordLength ||
            !TryReadWide(payload.Slice(2, WideNameLength), out string name)) return false;
        group = new BuddyGroupRecord(
            BinaryPrimitives.ReadUInt16LittleEndian(payload), name,
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(42)));
        return name.Length != 0;
    }

    public static bool TryParseRenameGroup(
        ReadOnlySpan<byte> payload, out string oldName, out string newName)
    {
        oldName = newName = "";
        return payload.Length == WideNameLength * 2 &&
            TryReadWide(payload[..WideNameLength], out oldName) &&
            TryReadWide(payload[WideNameLength..], out newName) &&
            oldName.Length != 0 && newName.Length != 0;
    }

    public static bool TryParseGroupMembers(
        ReadOnlySpan<byte> payload, out string[] accountIds, out string groupName)
    {
        accountIds = Array.Empty<string>();
        groupName = "";
        if (payload.Length < 2 + WideNameLength) return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (count > 500 || payload.Length != 2 + count * AccountIdLength + WideNameLength)
            return false;
        var ids = new string[count];
        for (int i = 0; i < count; i++)
            if (!TryReadAnsi(payload.Slice(2 + i * AccountIdLength, AccountIdLength), out ids[i]))
                return false;
        if (!TryReadWide(payload[^WideNameLength..], out groupName) || groupName.Length == 0)
            return false;
        accountIds = ids;
        return true;
    }

    private static void WriteFriend(Span<byte> destination, BuddyFriendRecord friend)
    {
        WriteAnsi(destination[..AccountIdLength], friend.AccountId);
        WriteWide(destination.Slice(20, WideNameLength), friend.DisplayName);
        WriteWide(destination.Slice(60, WideNameLength), friend.GroupName);
        friend.Extension.AsSpan(0, Math.Min(ExtensionLength, friend.Extension.Length))
            .CopyTo(destination.Slice(116, ExtensionLength));
    }

    private static void WriteAnsi(Span<byte> destination, string value)
    {
        destination.Clear();
        Encoding.Latin1.GetBytes(value.AsSpan(0, Math.Min(value.Length, destination.Length)), destination);
    }

    private static void WriteWide(Span<byte> destination, string value)
    {
        destination.Clear();
        Encoding.Unicode.GetBytes(value.AsSpan(0, Math.Min(value.Length, destination.Length / 2)), destination);
    }

    private static bool TryReadAnsi(ReadOnlySpan<byte> source, out string value)
    {
        int end = source.IndexOf((byte)0);
        if (end < 0) end = source.Length;
        value = Encoding.Latin1.GetString(source[..end]);
        return value.Length is > 0 and <= 16 && IsIdentity(value);
    }

    private static bool TryReadWide(ReadOnlySpan<byte> source, out string value)
    {
        int end = 0;
        while (end + 1 < source.Length && (source[end] != 0 || source[end + 1] != 0)) end += 2;
        value = Encoding.Unicode.GetString(source[..end]);
        return value.Length <= 20 && value.IndexOf('\0') < 0;
    }

    private static bool IsIdentity(string value)
    {
        foreach (char character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) return false;
        return true;
    }
}

public static class BuddyPresenceCodec
{
    public static byte[] BuildVipEndpoint(IPEndPoint endpoint)
    {
        byte[] payload = new byte[6];
        WriteEndpoint(payload, endpoint);
        return payload;
    }

    public static byte[] BuildState(string accountId, IPEndPoint? endpoint)
    {
        byte[] payload = new byte[2 + (endpoint == null ? 21 : 33)];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, 1);
        Encoding.Latin1.GetBytes(
            accountId.AsSpan(0, Math.Min(accountId.Length, 20)), payload.AsSpan(2, 20));
        if (endpoint == null) return payload;
        payload[22] = 1;
        WriteEndpoint(payload.AsSpan(23, 6), endpoint);
        WriteEndpoint(payload.AsSpan(29, 6), endpoint);
        return payload;
    }

    private static void WriteEndpoint(Span<byte> destination, IPEndPoint endpoint)
    {
        byte[] address = endpoint.Address.MapToIPv4().GetAddressBytes();
        address.CopyTo(destination);
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..], checked((ushort)endpoint.Port));
    }
}
