using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy;

public sealed partial class BuddyServer
{
    private async Task RelayTunnelAsync(BuddyConnection sender, byte[] payload)
    {
        if (!sender.Authenticated || !BuddyTunnelCodec.TryParseRequest(payload, out var request) ||
            !AllowTunnel(sender))
        {
            Log.Warn("buddy-tunnel", "sender='{0}' rejeitado", sender.AccountId);
            return;
        }

        IReadOnlyList<BuddyFriendRecord> senderFriends =
            await _database.LoadFriendsAsync(sender.AccountId);
        var allowed = new HashSet<string>(
            senderFriends.Select(friend => friend.AccountId), StringComparer.OrdinalIgnoreCase);
        int delivered = 0;
        foreach (string recipient in request.Recipients.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool related = allowed.Contains(recipient);
            if (!BuddyTunnelPolicy.CanRelay(request.InnerOpcode, related) ||
                !_online.TryGetValue(recipient, out BuddyConnection? target)) continue;
            BuddyFriendRecord? targetView = related
                ? (await _database.LoadFriendsAsync(recipient)).FirstOrDefault(friend =>
                    string.Equals(friend.AccountId, sender.AccountId,
                        StringComparison.OrdinalIgnoreCase))
                : new BuddyFriendRecord(sender.AccountId, sender.DisplayName, "", []);
            if (targetView == null) continue;
            byte[] notification = BuddyTunnelCodec.BuildNotification(
                targetView, request.InnerOpcode, request.InnerPayload);
            if (await TrySendNotificationAsync(
                    target, BuddyProtocol.NTF_TUNNEL_PACKET, notification))
                delivered++;
        }
        Log.Debug("buddy-tunnel", "sender='{0}' opcode=0x{1:X4} targets={2} delivered={3}",
            sender.AccountId, request.InnerOpcode, request.Recipients.Length, delivered);
    }

    private static bool AllowTunnel(BuddyConnection connection)
    {
        long now = Environment.TickCount64;
        if (now - connection.TunnelWindowStart >= 5000)
        {
            connection.TunnelWindowStart = now;
            connection.TunnelPackets = 0;
        }
        connection.TunnelPackets++;
        return connection.TunnelPackets <= 60;
    }
}
