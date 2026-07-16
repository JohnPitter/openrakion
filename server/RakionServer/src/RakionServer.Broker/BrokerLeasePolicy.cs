using System;

namespace BrokenServer;

public static class BrokerLeasePolicy
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static bool IsExpired(Systems.SRX_Serverinfo server, DateTime nowUtc) =>
        server.status != 0 && server.lastPing + Timeout < nowUtc;
}
