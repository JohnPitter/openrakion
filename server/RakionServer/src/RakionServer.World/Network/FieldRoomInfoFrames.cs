using System;
using System.Collections.Generic;
using System.Text;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static class FieldRoomInfoFrames
    {
        public static IReadOnlyList<byte[]> Responses(FieldRoomInfoSnapshot snapshot)
        {
            var responses = new List<byte[]>(26)
            {
                Line(FormattableString.Invariant($"ID[{snapshot.Id}] Status[{snapshot.Status}]")),
                Line($"Char[{snapshot.CreatorCharacter}] Title[{snapshot.Title}]"),
                Line($"Password[{snapshot.Password}]"),
                Line(FormattableString.Invariant(
                    $"Level[{snapshot.MinLevel}~{snapshot.MaxLevel}] Basic[{snapshot.Basic}] Map[{snapshot.Map}] Mode[{snapshot.Mode}]")),
                Line(FormattableString.Invariant(
                    $"Boss[{snapshot.Boss}] Tunneling[{snapshot.Tunneling}]")),
                Line(FormattableString.Invariant(
                    $"OnVote[{snapshot.OnVote}] VotePos[{snapshot.VotePosition}] BanSlot[{snapshot.BanSlot}]"))
            };

            for (int index = 0; index < 0x14; index++)
            {
                FieldRoomInfoSlotSnapshot slot = index < snapshot.Slots.Length
                    ? snapshot.Slots[index]
                    : default;
                responses.Add(Line(FormattableString.Invariant(
                    $"Slot[{index}] ID[{slot.UserId}] Status[{slot.Status}] Auth[{slot.Auth}] Vote[{slot.Vote}]")));
            }
            return responses;
        }

        public static byte[] Line(string text)
        {
            using var writer = new PacketWriter();
            return writer.WriteWord(0x22)
                .WriteByte(0)
                .WriteBytes(Encoding.ASCII.GetBytes(text))
                .ToArray();
        }
    }
}
