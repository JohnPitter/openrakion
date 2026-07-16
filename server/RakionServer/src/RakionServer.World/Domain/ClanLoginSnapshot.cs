using System;
using System.Collections.Generic;

namespace RakionServer.World.Domain;

public sealed record ClanTreeChild(string AccountName, string CharacterName);

public sealed record ClanLoginSnapshot
{
    public static ClanLoginSnapshot Empty { get; } = new();

    public int Id { get; init; }
    public byte Grade { get; init; }
    public string Name { get; init; } = "";
    public uint Rank { get; init; }
    public ushort Members { get; init; }
    public uint Point { get; init; }
    public uint MemberPoint { get; init; }
    public uint MemberRank { get; init; }
    public string MasterCharacterName { get; init; } = "";
    public string TreeUpperAccount { get; init; } = "";
    public string TreeUpperCharacter { get; init; } = "";
    public byte TreeRank { get; init; }
    public IReadOnlyList<ClanTreeChild> Children { get; init; } = Array.Empty<ClanTreeChild>();
}
