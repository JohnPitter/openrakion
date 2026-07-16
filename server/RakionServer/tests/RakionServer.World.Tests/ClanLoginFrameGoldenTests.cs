using System;
using RakionServer.World.CharSelect;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ClanLoginFrameGoldenTests
{
    [Fact]
    public void ClanAndTreeHeaderMatchesOriginalDifferentialLayout()
    {
        var clan = new ClanLoginSnapshot
        {
            Id = 0x11223344,
            Name = "ProbeClan",
            Rank = 0x5566,
            Members = 1,
            Point = 0x01020304,
            MemberPoint = 0x55667788,
            MemberRank = 0x3344,
            MasterCharacterName = "MasterZZ",
            TreeUpperAccount = "parent",
            TreeUpperCharacter = "ParentChar",
            TreeRank = 5,
            Children = [
                new ClanTreeChild("childone", "ChildOne"),
                new ClanTreeChild("childtwo", "ChildTwo")
            ]
        };
        byte[] frame = LoginCharListWriter.Build(new CharList
        {
            DisplayName = "BuddyX",
            Clan = clan,
            PowerTimeMarker = 0xAABBCCDD,
            PowerLevelPoint = 0x5566,
            Country = 0x7788,
            Gold = 0x01020304,
            Cash = 0x55667788,
            SlotCount = 4
        });

        const string expected =
            "4433221150726F6265436C616E00665500000100040302018877665544330000" +
            "4D61737465725A5A0042756464795800DDCCBBAA66558877706172656E7400" +
            "506172656E74436861720005026368696C646F6E65004368696C644F6E6500" +
            "6368696C6474776F004368696C6454776F00040302018877665504";
        int headerLength = frame.Length - 20;
        Assert.Equal(expected, Convert.ToHexString(frame.AsSpan(17, headerLength - 17)));
        Assert.Equal(0x03, frame[headerLength]);
    }

    [Fact]
    public void ClanTreeRejectsMoreThanSevenChildren()
    {
        var children = new ClanTreeChild[8];
        Array.Fill(children, new ClanTreeChild("child", "Character"));
        var list = new CharList
        {
            Clan = new ClanLoginSnapshot { Children = children }
        };

        Assert.Throws<ArgumentException>(() => LoginCharListWriter.Build(list));
    }
}
