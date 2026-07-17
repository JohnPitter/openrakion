using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class StageResultProtocolTests
{
    [Fact]
    public void ParsesCompleteStageResult()
    {
        byte[] body = Convert.FromHexString(
            "020402341278560A000000140000001E0000002800000032000000");

        bool parsed = StageResultProtocol.TryParse(body, out StageResultReport? report, out var error);

        Assert.True(parsed);
        Assert.Equal(StageResultParseError.None, error);
        Assert.NotNull(report);
        Assert.Equal(new ushort[] { 0x1234, 0x5678 }, report.MapSlots);
        Assert.Equal((uint)10, report.ReportedExp);
        Assert.Equal((uint)50, report.CellExpSlot3);
    }

    [Fact]
    public void ParsesCanonicalEncryptedPadding()
    {
        byte[] body = Convert.FromHexString(
            "0304002800000053000000000000000000000000000000A1B2C3D4E5F6071829");

        bool parsed = StageResultProtocol.TryParse(body, out StageResultReport? report, out var error);

        Assert.True(parsed);
        Assert.Equal(StageResultParseError.None, error);
        Assert.NotNull(report);
        Assert.Empty(report.MapSlots);
        Assert.Equal((uint)40, report.ReportedExp);
        Assert.Equal((uint)83, report.ReportedGold);
    }

    [Theory]
    [InlineData("", StageResultParseError.HeaderTruncated)]
    [InlineData("640000", StageResultParseError.StageOutOfRange)]
    [InlineData("000600", StageResultParseError.RankOutOfRange)]
    [InlineData("000005", StageResultParseError.SlotCountOutOfRange)]
    [InlineData("000000", StageResultParseError.LengthMismatch)]
    [InlineData("000000000000000000000000000000000000000000000000", StageResultParseError.LengthMismatch)]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000000", StageResultParseError.LengthMismatch)]
    public void RejectsInvalidShape(string hex, StageResultParseError expected)
    {
        bool parsed = StageResultProtocol.TryParse(
            Convert.FromHexString(hex), out StageResultReport? report, out var error);

        Assert.False(parsed);
        Assert.Null(report);
        Assert.Equal(expected, error);
    }
}
