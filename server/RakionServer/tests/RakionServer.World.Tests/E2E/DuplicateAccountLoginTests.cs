using System;
using System.Threading.Tasks;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E;

[Collection("E2E")]
public sealed class DuplicateAccountLoginTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SecondSessionForSameAccountIsRejected()
    {
        await using WorldServerFixture fixture = await WorldServerFixture.CreateAsync();
        if (!fixture.Available) return;
        await using HeadlessWorldClient first = await HeadlessWorldClient.ConnectAsync(
            WorldServerFixture.Host, fixture.TcpPort, "duplicate-first");
        await using HeadlessWorldClient second = await HeadlessWorldClient.ConnectAsync(
            WorldServerFixture.Host, fixture.TcpPort, "duplicate-second");

        first.Login("test", "test");
        first.WaitForFirstByte(0x0C, Timeout);
        second.Login("test", "test");

        byte[] error = second.WaitForNext(frame =>
            frame.Length >= 4 && frame[0] == Protocol.LoginError.Category &&
            frame[2] == Protocol.LoginError.Main, Timeout);
        Assert.Equal(Protocol.LoginError.SubAccountInUse, error[3]);
        Assert.Equal(1, fixture.Server!.CurrentUsers);
    }
}
