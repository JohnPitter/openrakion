using System;

namespace RakionServer.World.Network
{
    public readonly record struct SuccessUdpRequest(byte Result)
    {
        public static bool TryParse(ReadOnlySpan<byte> payload, out SuccessUdpRequest request)
        {
            request = default;
            if (payload.IsEmpty) return false;
            request = new SuccessUdpRequest(payload[0]);
            return true;
        }
    }
}
