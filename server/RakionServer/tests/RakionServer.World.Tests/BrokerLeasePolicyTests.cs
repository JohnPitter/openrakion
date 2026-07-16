using System;
using System.Collections.Generic;
using BrokenServer;
using Xunit;

namespace RakionServer.World.Tests;

[Collection(BrokerTestCollection.Name)]
public sealed class BrokerLeasePolicyTests
{
    [Fact]
    public void OnlineWorldExpiresOnlyAfterFiveMinutes()
    {
        DateTime heartbeat = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        var server = new Systems.SRX_Serverinfo { status = 1, lastPing = heartbeat };

        Assert.False(BrokerLeasePolicy.IsExpired(
            server, heartbeat + BrokerLeasePolicy.Timeout));
        Assert.True(BrokerLeasePolicy.IsExpired(
            server, heartbeat + BrokerLeasePolicy.Timeout + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void OfflineWorldDoesNotExpireAgain()
    {
        var server = new Systems.SRX_Serverinfo
        {
            status = 0,
            lastPing = DateTime.UnixEpoch
        };

        Assert.False(BrokerLeasePolicy.IsExpired(server, DateTime.MaxValue));
    }

    [Fact]
    public void EndpointLookupRequiresExactConfiguredIpAndPort()
    {
        Dictionary<int, Systems.SRX_Serverinfo> previous = Systems.GSList;
        try
        {
            var server = new Systems.SRX_Serverinfo { ip = "127.0.0.1", ipcport = 40708 };
            Systems.GSList = new Dictionary<int, Systems.SRX_Serverinfo> { [1] = server };

            Assert.Same(server, Systems.GetServerByEndPoint("127.0.0.1", 40708));
            Assert.Null(Systems.GetServerByEndPoint("127.0.0.2", 40708));
            Assert.Null(Systems.GetServerByEndPoint("127.0.0.1", 40709));
        }
        finally
        {
            Systems.GSList = previous;
        }
    }
}
