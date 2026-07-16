using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace RakionServer.Buddy;

public sealed record BuddyTunnelRequest(
    byte Flags, ushort InnerOpcode, byte[] InnerPayload, string[] Recipients);

public static class BuddyTunnelPolicy
{
    public static bool CanRelay(ushort innerOpcode, bool recipientsAreFriends) =>
        recipientsAreFriends || innerOpcode is 0xC041 or 0xC042;
}

public static class BuddyTunnelCodec
{
    public const int NotificationPrefixLength = 104;

    public static bool TryParseRequest(ReadOnlySpan<byte> payload, out BuddyTunnelRequest request)
    {
        request = new BuddyTunnelRequest(0, 0, [], []);
        if (payload.Length < 7) return false;
        byte flags = payload[0];
        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1));
        int innerLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(3));
        if (innerLength > 255 || payload.Length < 7 + innerLength) return false;
        int countOffset = 5 + innerLength;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(countOffset));
        if (count is < 1 or > 500 || payload.Length != countOffset + 2 + count * 20)
            return false;
        var recipients = new string[count];
        for (int i = 0; i < count; i++)
            if (!TryReadIdentity(payload.Slice(countOffset + 2 + i * 20, 20), out recipients[i]))
                return false;
        request = new BuddyTunnelRequest(
            flags, opcode, payload.Slice(5, innerLength).ToArray(), recipients);
        return true;
    }

    public static byte[] BuildNotification(
        BuddyFriendRecord sender, ushort innerOpcode, ReadOnlySpan<byte> innerPayload)
    {
        if (innerPayload.Length > 255) throw new ArgumentOutOfRangeException(nameof(innerPayload));
        byte[] payload = new byte[NotificationPrefixLength + innerPayload.Length];
        WriteAnsi(payload.AsSpan(0, 20), sender.AccountId);
        WriteWide(payload.AsSpan(20, 40), sender.DisplayName);
        WriteWide(payload.AsSpan(60, 40), sender.GroupName);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(100), (ushort)innerPayload.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(102), innerOpcode);
        innerPayload.CopyTo(payload.AsSpan(NotificationPrefixLength));
        return payload;
    }

    private static bool TryReadIdentity(ReadOnlySpan<byte> source, out string value)
    {
        int end = source.IndexOf((byte)0);
        if (end < 0) end = source.Length;
        value = Encoding.Latin1.GetString(source[..end]);
        if (value.Length is < 1 or > 16) return false;
        foreach (char character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) return false;
        return true;
    }

    private static void WriteAnsi(Span<byte> destination, string value)
    {
        Encoding.Latin1.GetBytes(
            value.AsSpan(0, Math.Min(value.Length, destination.Length)), destination);
    }

    private static void WriteWide(Span<byte> destination, string value)
    {
        Encoding.Unicode.GetBytes(
            value.AsSpan(0, Math.Min(value.Length, destination.Length / 2)), destination);
    }
}
