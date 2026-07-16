using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    public sealed partial class BuddyServer
    {
        private readonly BuddyConfig _config;
        private readonly BuddyDatabase _database;
        private readonly ChatModerationEngine _moderation;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Socket> _listeners = [];
        private readonly ConcurrentDictionary<string, BuddyConnection> _online =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<uint, BuddyConnection> _udpTokens = new();

        public BuddyServer(BuddyConfig config, BuddyDatabase database)
        {
            _config = config;
            _database = database;
            IReadOnlyList<ChatAbuseRule> rules = ChatModerationEngine.LoadRules(config.AbuseFile);
            _moderation = new ChatModerationEngine(config.Moderation, rules);
        }

        public void Start()
        {
            foreach (int port in _config.Ports)
            {
                var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Bind(new IPEndPoint(IPAddress.Any, port));
                listener.Listen(64);
                _listeners.Add(listener);
                Log.Ok("buddy", "ouvindo na porta {0}", port);
                _ = Task.Run(() => AcceptLoopAsync(listener, port, _cts.Token));

                var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                udp.Bind(new IPEndPoint(IPAddress.Any, port));
                _listeners.Add(udp);
                Log.Ok("buddy", "ouvindo UDP na porta {0}", port);
                _ = Task.Run(() => UdpLoopAsync(udp, port, _cts.Token));
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            foreach (Socket listener in _listeners)
                try { listener.Close(); } catch (SocketException) { }
        }

        private async Task AcceptLoopAsync(Socket listener, int port, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Socket socket = await listener.AcceptAsync(cancellationToken);
                    _ = Task.Run(() => HandleAsync(socket, port, cancellationToken));
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception exception)
                {
                    Log.Error("buddy", "accept {0}: {1}", port, exception.Message);
                }
            }
        }

        private async Task HandleAsync(Socket socket, int port, CancellationToken cancellationToken)
        {
            string ip = (socket.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
            var connection = new BuddyConnection(socket, ip);
            Log.Info("buddy", "[{0}] conectado em :{1}", ip, port);
            byte[] buffer = new byte[ushort.MaxValue];
            int buffered = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int received = await socket.ReceiveAsync(
                        buffer.AsMemory(buffered, buffer.Length - buffered),
                        SocketFlags.None, cancellationToken);
                    if (received == 0) break;
                    buffered += received;
                    int consumed = await ProcessFramesAsync(connection, buffer, buffered);
                    if (consumed > 0)
                    {
                        Buffer.BlockCopy(buffer, consumed, buffer, 0, buffered - consumed);
                        buffered -= consumed;
                    }
                    else if (buffered == buffer.Length)
                    {
                        Log.Warn("buddy", "[{0}] frame excedeu o buffer", ip);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (Exception exception)
            {
                Log.Error("buddy", "[{0}] fluxo abortado: {1}", ip, exception.Message);
            }
            finally
            {
                await RemoveOnlineAsync(connection);
                try { socket.Close(); } catch (SocketException) { }
                Log.Info("buddy", "[{0}] desconectado account='{1}'", ip, connection.AccountId);
            }
        }

        private async Task<int> ProcessFramesAsync(
            BuddyConnection connection, byte[] buffer, int buffered)
        {
            int consumed = 0;
            while (buffered - consumed >= BuddyProtocol.HeaderSize)
            {
                ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(consumed));
                if (size < BuddyProtocol.HeaderSize)
                    throw new InvalidOperationException($"frame Buddy inválido: {size}");
                if (buffered - consumed < size) break;
                ushort command = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(consumed + 2));
                byte[] payload = buffer.AsSpan(consumed + 4, size - 4).ToArray();
                consumed += size;
                await DispatchAsync(connection, command, payload);
            }
            return consumed;
        }

        private async Task SendAsync(BuddyConnection connection, ushort command, byte[] payload)
        {
            int size = BuddyProtocol.HeaderSize + payload.Length;
            if (size > ushort.MaxValue) throw new InvalidOperationException("frame Buddy excede u16");
            byte[] frame = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)size);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), command);
            payload.CopyTo(frame, 4);
            await connection.SendLock.WaitAsync(_cts.Token);
            try
            {
                int sent = 0;
                while (sent < frame.Length)
                    sent += await connection.Socket.SendAsync(
                        frame.AsMemory(sent), SocketFlags.None, _cts.Token);
            }
            finally
            {
                connection.SendLock.Release();
            }
        }

        private async Task<bool> TrySendNotificationAsync(
            BuddyConnection connection, ushort command, byte[] payload)
        {
            try
            {
                await SendAsync(connection, command, payload);
                return true;
            }
            catch (Exception exception) when (
                exception is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                return false;
            }
        }

        private async Task RemoveOnlineAsync(BuddyConnection connection)
        {
            if (connection.AccountId.Length == 0) return;
            if (connection.UdpToken != 0)
                _udpTokens.TryRemove(connection.UdpToken, out _);
            _online.TryGetValue(connection.AccountId, out BuddyConnection? current);
            if (!ReferenceEquals(current, connection)) return;
            _online.TryRemove(connection.AccountId, out _);
            if (connection.UdpEndpoint == null || _cts.IsCancellationRequested) return;
            try { await PublishPresenceAsync(connection, null, false); }
            catch (Exception exception)
            {
                Log.Warn("buddy-presence", "offline account='{0}': {1}",
                    connection.AccountId, exception.Message);
            }
        }
    }
}
