using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    public static class GmOperationPolicy
    {
        public const byte RequiredSubStatus = 0x34;
        public const byte WrongSubStatusReason = 0xB9;
        public const byte IpDeniedReason = 0xBA;

        public static byte? DisconnectReason(
            byte subStatus, string remoteIp, IReadOnlySet<string> allowedIps)
        {
            if (subStatus != RequiredSubStatus) return WrongSubStatusReason;
            return allowedIps.Contains(remoteIp) ? null : IpDeniedReason;
        }
    }
}
