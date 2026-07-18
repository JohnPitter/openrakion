using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Buddy;
using RakionServer.Common;

namespace RakionServer.World.Tests;

internal sealed record BuddyFrame(ushort Command, byte[] Payload);

internal sealed class BuddyHeadlessClient : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly Dictionary<ushort, Queue<BuddyFrame>> _pending = [];
    private NetworkStream? _stream;

    public PacketCrypto Crypto { get; private set; } = new();

    public async Task ConnectAndLoginAsync(string accountId, int port)
    {
        await _tcp.ConnectAsync(IPAddress.Loopback, port);
        _stream = _tcp.GetStream();
        await SendAsync(BuddyProtocol.SVC_PRECREDENTIAL, []);
        BuddyFrame precredential = await ReadUntilAsync(BuddyProtocol.RET_PRECREDENTIAL);
        uint seed = BinaryPrimitives.ReadUInt32LittleEndian(precredential.Payload);

        Crypto = new PacketCrypto();
        Crypto.Enable(BuddyCrypto.DeriveSessionKey(accountId, seed), BuddyCrypto.SessionMarker);
        byte[] login = new byte[BuddyCrypto.LoginPayloadLength];
        BuddyCrypto.CreateCredential(accountId, seed).CopyTo(login, 0);
        byte[] clear = new byte[0x84];
        BinaryPrimitives.WriteUInt32LittleEndian(clear, 0x1B);
        Crypto.Encrypt(clear).CopyTo(login, BuddyCrypto.CredentialLength);
        await SendAsync(BuddyProtocol.SVC_LOGIN, login);
    }

    public async Task RegisterUdpAsync(uint token, int port)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, token);
        await _udp.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, port));
    }

    public async Task SendAsync(ushort command, byte[] payload)
    {
        NetworkStream stream = _stream ?? throw new InvalidOperationException("cliente desconectado");
        byte[] frame = new byte[BuddyProtocol.HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), command);
        payload.CopyTo(frame, BuddyProtocol.HeaderSize);
        await stream.WriteAsync(frame);
    }

    public async Task<BuddyFrame> ReadUntilAsync(ushort command)
    {
        if (_pending.TryGetValue(command, out Queue<BuddyFrame>? frames) && frames.Count > 0)
            return frames.Dequeue();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            BuddyFrame frame = await ReadFrameAsync(timeout.Token);
            if (frame.Command == command) return frame;
            if (!_pending.TryGetValue(frame.Command, out frames))
            {
                frames = new Queue<BuddyFrame>();
                _pending.Add(frame.Command, frames);
            }
            frames.Enqueue(frame);
        }
    }

    public ValueTask DisposeAsync()
    {
        _udp.Dispose();
        _tcp.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<BuddyFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new InvalidOperationException("cliente desconectado");
        byte[] header = new byte[BuddyProtocol.HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (size < BuddyProtocol.HeaderSize)
            throw new InvalidDataException($"frame Buddy inválido: {size}");
        byte[] payload = new byte[size - BuddyProtocol.HeaderSize];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return new BuddyFrame(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)), payload);
    }
}
