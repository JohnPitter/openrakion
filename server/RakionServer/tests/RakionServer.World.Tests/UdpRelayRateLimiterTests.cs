using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class UdpRelayRateLimiterTests
{
    [Fact]
    public void BurstIsBoundedAndTokensRefillOverTime()
    {
        var limiter = new UdpRelayRateLimiter(10, 20, 1000);

        for (int i = 0; i < 20; i++) Assert.True(limiter.TryConsume(1000));
        Assert.False(limiter.TryConsume(1000));
        for (int i = 0; i < 5; i++) Assert.True(limiter.TryConsume(1500));
        Assert.False(limiter.TryConsume(1500));
        Assert.True(limiter.TryConsume(2500));
    }

    [Fact]
    public void ReusedSlotGetsFreshBucketForNewAuthenticatedSession()
    {
        var registry = new UdpRelayLimiterRegistry(1, 2);

        Assert.True(registry.TryConsume(7, 100, 1000));
        Assert.True(registry.TryConsume(7, 100, 1000));
        Assert.False(registry.TryConsume(7, 100, 1000));

        Assert.True(registry.TryConsume(7, 200, 1000));
        Assert.True(registry.TryConsume(7, 200, 1000));
        Assert.False(registry.TryConsume(7, 200, 1000));
    }
}
