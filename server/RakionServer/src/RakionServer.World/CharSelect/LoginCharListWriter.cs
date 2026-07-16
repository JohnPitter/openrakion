using System;
using System.Buffers.Binary;
using System.Text;
using RakionServer.World.Domain;

namespace RakionServer.World.CharSelect;

public static class LoginCharListWriter
{
    private const int FixedPrefixSize = 21;
    private const int TrailerSize = 20;
    private const int NamePrefix = 5;
    private const int FieldsSize = 359;
    private const int Win = 0, Lose = 4, Draw = 8, Class = 22, Level = 23;
    private const int Exp = 24, LevelPoint = 28, Stats = 30, Equip = 50;
    private const int Quickslot = 76, Enhance = 88, Ranks = 260;

    public static byte[] Build(CharList list)
    {
        int headerSize = HeaderSize(list);
        int total = headerSize + TrailerSize;
        foreach (CharSummary character in list.Chars) total += RecordSize(character.Name);
        var buffer = new byte[total];

        WriteHeader(buffer, list);
        int offset = headerSize;
        foreach (CharSummary character in list.Chars)
            offset = WriteRecord(buffer, offset, character);
        buffer[offset] = 0x03;
        return buffer;
    }

    private static int HeaderSize(CharList list)
    {
        ClanLoginSnapshot clan = list.Clan;
        if (clan.Children.Count > 7)
            throw new ArgumentException("A árvore de clã excede sete filhos.", nameof(list));
        int size = FixedPrefixSize + Length(clan.Name, 12, nameof(clan.Name)) + 1 + 18;
        size += Length(clan.MasterCharacterName, 12, nameof(clan.MasterCharacterName)) + 1;
        size += Length(list.DisplayName, 12, nameof(list.DisplayName)) + 1 + 8;
        size += Length(clan.TreeUpperAccount, 16, nameof(clan.TreeUpperAccount)) + 1;
        size += Length(clan.TreeUpperCharacter, 12, nameof(clan.TreeUpperCharacter)) + 3;
        foreach (ClanTreeChild child in clan.Children)
            size += Length(child.AccountName, 16, nameof(child.AccountName)) +
                Length(child.CharacterName, 12, nameof(child.CharacterName)) + 2;
        return size + 9;
    }

    private static void WriteHeader(byte[] buffer, CharList list)
    {
        buffer[0] = 0x0C;
        buffer[3] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(7), list.NetworkSlot);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(9), list.UdpSessionKey);
        if (list.SessionHandle.Length >= 4)
            list.SessionHandle.AsSpan(0, 4).CopyTo(buffer.AsSpan(13));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(17), list.Clan.Id);

        int offset = FixedPrefixSize;
        offset = WriteClan(buffer, offset, list.Clan);
        offset = WriteCString(buffer, offset, list.DisplayName);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), list.PowerTimeMarker);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset + 4), list.PowerLevelPoint);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset + 6), list.Country);
        offset = WriteTree(buffer, offset + 8, list.Clan);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), list.Gold);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 4), list.Cash);
        buffer[offset + 8] = list.SlotCount;
    }

    private static int WriteClan(byte[] buffer, int offset, ClanLoginSnapshot clan)
    {
        offset = WriteCString(buffer, offset, clan.Name);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), clan.Rank);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset + 4), clan.Members);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 6), clan.Point);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 10), clan.MemberPoint);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 14), clan.MemberRank);
        return WriteCString(buffer, offset + 18, clan.MasterCharacterName);
    }

    private static int WriteTree(byte[] buffer, int offset, ClanLoginSnapshot clan)
    {
        offset = WriteCString(buffer, offset, clan.TreeUpperAccount);
        offset = WriteCString(buffer, offset, clan.TreeUpperCharacter);
        buffer[offset++] = clan.TreeRank;
        buffer[offset++] = checked((byte)clan.Children.Count);
        foreach (ClanTreeChild child in clan.Children)
        {
            offset = WriteCString(buffer, offset, child.AccountName);
            offset = WriteCString(buffer, offset, child.CharacterName);
        }
        return offset;
    }

    private static int RecordSize(string name) => NamePrefix + Length(name, 12, nameof(name)) + 1 + FieldsSize;

    private static int WriteRecord(byte[] buffer, int record, CharSummary character)
    {
        int nameLength = WriteCString(buffer, record + NamePrefix, character.Name) - record - NamePrefix - 1;
        buffer[record] = (byte)(2 * character.Slot + 1);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(record + 1), character.CharacterId);
        int fields = record + NamePrefix + nameLength + 1;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(fields + Win), character.Win);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(fields + Lose), character.Lose);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(fields + Draw), character.Draw);
        buffer[fields + Class] = character.Class;
        buffer[fields + Level] = character.Level;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(fields + Exp), character.Exp);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(fields + LevelPoint), character.LevelPoint);
        WriteU16(buffer, fields + Stats, character.Stats, 10);
        WriteU16(buffer, fields + Equip, character.Equip, 7);
        WriteU16(buffer, fields + Quickslot, character.Quickslot, 6);
        WriteU8(buffer, fields + Enhance, character.Enhance, 7);
        if (character.StageRanks.Length > 1)
            Array.Copy(character.StageRanks, 1, buffer, fields + Ranks,
                Math.Min(character.StageRanks.Length - 1, FieldsSize - Ranks));
        return record + RecordSize(character.Name);
    }

    private static void WriteU16(byte[] buffer, int offset, ushort[] source, int count)
    {
        for (int i = 0; i < count && i < source.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset + i * 2), source[i]);
    }

    private static void WriteU8(byte[] buffer, int offset, byte[] source, int count)
    {
        for (int i = 0; i < count && i < source.Length; i++)
            buffer[offset + i] = source[i];
    }

    private static int WriteCString(byte[] buffer, int offset, string value)
    {
        int length = Encoding.ASCII.GetBytes(value, buffer.AsSpan(offset));
        return offset + length + 1;
    }

    private static int Length(string value, int maximum, string parameter)
    {
        int length = Encoding.ASCII.GetByteCount(value);
        if (length > maximum)
            throw new ArgumentException($"{parameter} excede {maximum} bytes ASCII.", parameter);
        return length;
    }
}
