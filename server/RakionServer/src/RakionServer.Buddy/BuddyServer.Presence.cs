using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy;

public sealed partial class BuddyServer
{
    private uint IssueUdpToken(BuddyConnection connection)
    {
        if (connection.UdpToken != 0)
            _udpTokens.TryRemove(connection.UdpToken, out _);
        uint token;
        do { token = RandomUInt32(); }
        while (!_udpTokens.TryAdd(token, connection));
        connection.UdpToken = token;
        return token;
    }

    private async Task UdpLoopAsync(Socket socket, int port, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, any, cancellationToken);
                if (result.ReceivedBytes != 4 || result.RemoteEndPoint is not IPEndPoint endpoint)
                    continue;
                uint token = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                if (!_udpTokens.TryRemove(token, out BuddyConnection? connection) ||
                    !connection.Authenticated ||
                    !_online.TryGetValue(connection.AccountId, out BuddyConnection? current) ||
                    !ReferenceEquals(current, connection)) continue;
                connection.UdpEndpoint = endpoint;
                await SendAsync(connection, BuddyProtocol.NTF_VIP_IPPORT,
                    BuddyPresenceCodec.BuildVipEndpoint(endpoint));
                await PublishPresenceAsync(connection, endpoint, true);
                Log.Info("buddy-presence", "account='{0}' UDP observado em {1}:{2} via :{3}",
                    connection.AccountId, endpoint.Address, endpoint.Port, port);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                    Log.Warn("buddy-presence", "UDP :{0}: {1}", port, exception.Message);
            }
        }
    }

    private async Task PublishPresenceAsync(
        BuddyConnection source, IPEndPoint? endpoint, bool includeReverse)
    {
        var friends = await _database.LoadFriendsAsync(source.AccountId);
        byte[] sourceState = BuddyPresenceCodec.BuildState(source.AccountId, endpoint);
        foreach (BuddyFriendRecord friend in friends)
        {
            if (!_online.TryGetValue(friend.AccountId, out BuddyConnection? target) ||
                !target.Authenticated) continue;
            await TrySendNotificationAsync(target, BuddyProtocol.NTF_USER_STATE, sourceState);
            if (includeReverse && target.UdpEndpoint != null)
                await TrySendNotificationAsync(source, BuddyProtocol.NTF_USER_STATE,
                    BuddyPresenceCodec.BuildState(target.AccountId, target.UdpEndpoint));
        }
    }

    private async Task SyncFriendPresenceAsync(BuddyConnection source, string friendAccount)
    {
        if (!_online.TryGetValue(friendAccount, out BuddyConnection? target)) return;
        if (source.UdpEndpoint != null)
            await TrySendNotificationAsync(target, BuddyProtocol.NTF_USER_STATE,
                BuddyPresenceCodec.BuildState(source.AccountId, source.UdpEndpoint));
        if (target.UdpEndpoint != null)
            await TrySendNotificationAsync(source, BuddyProtocol.NTF_USER_STATE,
                BuddyPresenceCodec.BuildState(target.AccountId, target.UdpEndpoint));
    }
}
