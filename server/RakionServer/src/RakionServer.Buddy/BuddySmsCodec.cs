using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace RakionServer.Buddy
{
    public sealed record BuddySmsMessage(
        uint Id, string SenderAccount, string SenderDisplay,
        string TargetAccount, string Text, DateTime CreatedAtUtc);

    public static class BuddySmsCodec
    {
        public const ushort P2PSendSms = 0xC015;
        public const int MaxMessageBytes = 128;

        public static bool TryParseSend(
            ReadOnlySpan<byte> clear, out string target, out string text)
        {
            target = "";
            text = "";
            if (clear.Length < 22) return false;
            int targetLength = clear[..20].IndexOf((byte)0);
            if (targetLength <= 0) return false;
            int messageLength = BinaryPrimitives.ReadUInt16LittleEndian(clear.Slice(20, 2));
            if (messageLength is < 1 or > MaxMessageBytes || clear.Length < 22 + messageLength)
                return false;
            target = Encoding.Latin1.GetString(clear[..targetLength]);
            text = Encoding.Latin1.GetString(clear.Slice(22, messageLength));
            return IsIdentity(target);
        }

        public static byte[] BuildSend(string target, string text)
        {
            byte[] targetBytes = Encoding.Latin1.GetBytes(target);
            byte[] textBytes = Encoding.Latin1.GetBytes(text);
            int clearLength = RoundUp(22 + textBytes.Length, 12);
            byte[] clear = new byte[clearLength];
            targetBytes.AsSpan(0, Math.Min(19, targetBytes.Length)).CopyTo(clear);
            BinaryPrimitives.WriteUInt16LittleEndian(clear.AsSpan(20, 2), (ushort)textBytes.Length);
            textBytes.CopyTo(clear, 22);
            return clear;
        }

        public static byte[] BuildSavedBatch(IReadOnlyList<BuddySmsMessage> messages)
        {
            int length = 2;
            foreach (BuddySmsMessage message in messages)
                length += 72 + Encoding.Latin1.GetByteCount(message.Text);
            byte[] clear = new byte[length];
            BinaryPrimitives.WriteUInt16LittleEndian(clear, checked((ushort)messages.Count));
            int offset = 2;
            foreach (BuddySmsMessage message in messages)
                offset = WriteSavedRecord(clear, offset, message);
            return clear;
        }

        public static bool TryParseAcknowledgement(
            ReadOnlySpan<byte> payload, out uint[] messageIds)
        {
            messageIds = Array.Empty<uint>();
            if (payload.Length < 2) return false;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            if (count > 500 || payload.Length != 2 + count * 4) return false;
            messageIds = new uint[count];
            for (int i = 0; i < count; i++)
                messageIds[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2 + i * 4, 4));
            return true;
        }

        private static int WriteSavedRecord(byte[] destination, int offset, BuddySmsMessage message)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), message.Id);
            WriteLatinFixed(destination.AsSpan(offset + 4, 20), message.SenderAccount);
            WriteUtf16Fixed(destination.AsSpan(offset + 24, 40), message.SenderDisplay);
            long unix = new DateTimeOffset(message.CreatedAtUtc).ToUnixTimeSeconds();
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 64), checked((uint)unix));
            byte[] text = Encoding.Latin1.GetBytes(message.Text);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset + 68), P2PSendSms);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset + 70), checked((ushort)text.Length));
            text.CopyTo(destination, offset + 72);
            return offset + 72 + text.Length;
        }

        private static void WriteLatinFixed(Span<byte> destination, string value)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(value);
            bytes.AsSpan(0, Math.Min(destination.Length - 1, bytes.Length)).CopyTo(destination);
        }

        private static void WriteUtf16Fixed(Span<byte> destination, string value)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(value);
            int count = Math.Min(destination.Length - 2, bytes.Length) & ~1;
            bytes.AsSpan(0, count).CopyTo(destination);
        }

        private static int RoundUp(int value, int block) => (value + block - 1) / block * block;

        private static bool IsIdentity(string value)
        {
            if (value.Length is < 1 or > 16) return false;
            foreach (char character in value)
                if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) return false;
            return true;
        }
    }
}
