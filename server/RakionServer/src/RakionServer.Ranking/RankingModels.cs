using System;

namespace RakionServer.Ranking;

public sealed record CharacterRankSource(
    int Id,
    string Username,
    string Name,
    byte Level,
    byte Class,
    int Experience,
    int Wins,
    int Losses,
    int Draws,
    int LastTotalRank,
    int LastClassRank,
    short Country);

public sealed record CharacterRank(
    CharacterRankSource Source,
    int TotalRank,
    int ClassRank,
    byte Grade);

public sealed record ClanMemberRankSource(int UserGameId, int ClanId, int ClanPoints);

public sealed record ClanMemberRank(int UserGameId, int Rank);

public sealed record ClanRankSource(
    int Id,
    string Name,
    string Master,
    byte Members,
    DateTime CreatedAt,
    int Points,
    int LastRank,
    short Country);

public sealed record ClanRank(ClanRankSource Source, int Rank);

public sealed record RankingProjection(
    CharacterRank[] Characters,
    ClanMemberRank[] ClanMembers,
    ClanRank[] Clans);
