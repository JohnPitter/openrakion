using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RakionServer.World.BotEngine;

internal sealed class BotEngineClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private int _correlation;

    private BotEngineClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
    }

    public static async Task<BotEngineClient> ConnectAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            return new BotEngineClient(pipe);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<BotEngineHello> HelloAsync(CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.Hello,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != 12)
            throw new InvalidDataException("Hello do Bot Engine Host possui tamanho inválido.");
        return new BotEngineHello(
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload),
            (BotEngineProtocol.Capability)BinaryPrimitives.ReadUInt32LittleEndian(
                frame.Payload.AsSpan(4)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(8)));
    }

    public async Task LoadFieldAsync(
        BotEngineFieldRequest request,
        CancellationToken cancellationToken)
    {
        byte[] payload = BotEngineFrameCodec.EncodeLoadField(
            request.FieldId,
            request.MaximumBots,
            request.MapId,
            request.Mode,
            request.WorldName);
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.LoadField,
            payload,
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != 8 ||
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload) != request.FieldId ||
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4)) !=
                request.MaximumBots)
            throw new InvalidDataException("LoadField não confirmou field/capacidade.");
    }

    public async Task<BotEngineBot> AddBotAsync(
        BotEngineBotRequest request,
        CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.AddBot,
            BotEngineFrameCodec.EncodeAddBot(request),
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != 12)
            throw new InvalidDataException("AddBot retornou payload inválido.");
        var bot = new BotEngineBot(
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(8)));
        if (bot.BotId != request.BotId ||
            bot.Capacity == 0 ||
            bot.ActivePlayers == 0 ||
            bot.ActivePlayers > bot.Capacity)
            throw new InvalidDataException(
                "AddBot não confirmou identidade e capacidade do player.");
        return bot;
    }

    public async Task<BotEngineHealth> PingAsync(CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.Ping,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != 16)
            throw new InvalidDataException("Ping do Bot Engine Host possui tamanho inválido.");
        return new BotEngineHealth(
            BinaryPrimitives.ReadUInt64LittleEndian(frame.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(12)));
    }

    public async Task<BotEngineTick> TickAsync(
        uint frameCount,
        CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.Tick,
            BotEngineFrameCodec.EncodeTick(frameCount),
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != 8)
            throw new InvalidDataException("Tick retornou payload inválido.");
        var tick = new BotEngineTick(
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4)));
        if (tick.FrameCount != frameCount)
            throw new InvalidDataException("Tick não confirmou os frames solicitados.");
        return tick;
    }

    public async Task<BotEnginePlayerSnapshot> SnapshotAsync(
        uint botId,
        CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.Snapshot,
            BotEngineFrameCodec.EncodeSnapshot(botId),
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != BotEngineProtocol.SnapshotResponseSize)
            throw new InvalidDataException("Snapshot retornou payload inválido.");

        uint returnedBotId = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4));
        float[] values = ReadSnapshotValues(frame.Payload);
        if (returnedBotId != botId || flags > 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Snapshot retornou estado inválido.");
        return new BotEnginePlayerSnapshot(
            returnedBotId,
            (flags & 1) != 0,
            (flags & 2) != 0,
            values[0], values[1], values[2],
            values[3], values[4], values[5],
            values[6]);
    }

    public async Task<BotEngineInputResult> ApplyInputAsync(
        uint botId,
        BotEngineInput input,
        CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.Input,
            BotEngineFrameCodec.EncodeInput(botId, input),
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != 8)
            throw new InvalidDataException("Input retornou payload inválido.");
        var result = new BotEngineInputResult(
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload),
            (BotEngineInput)BinaryPrimitives.ReadUInt32LittleEndian(
                frame.Payload.AsSpan(4)));
        if (result.BotId != botId || result.Input != input)
            throw new InvalidDataException("Input não confirmou bot/flags.");
        return result;
    }

    public async Task<uint> AimAsync(
        BotEngineAim aim,
        CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.Aim,
            BotEngineFrameCodec.EncodeAim(aim),
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != sizeof(uint))
            throw new InvalidDataException("Aim retornou payload inválido.");
        uint botId = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload);
        if (botId != aim.BotId)
            throw new InvalidDataException("Aim não confirmou o bot.");
        return botId;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        BotEngineFrame frame = await RequestAsync(
            BotEngineProtocol.MessageType.Shutdown,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken).ConfigureAwait(false);
        if (frame.Payload.Length != 0)
            throw new InvalidDataException("Shutdown retornou payload inesperado.");
    }

    private async Task<BotEngineFrame> RequestAsync(
        BotEngineProtocol.MessageType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            uint correlation = unchecked((uint)Interlocked.Increment(ref _correlation));
            byte[] request = BotEngineFrameCodec.EncodeRequest(
                type, correlation, payload.Span);
            await _pipe.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            await _pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            BotEngineFrame response = await BotEngineFrameCodec.ReadResponseAsync(
                _pipe, type, correlation, cancellationToken).ConfigureAwait(false);
            if (response.Status != BotEngineProtocol.Status.Success)
                throw new BotEngineException(type, response.Status);
            return response;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static float[] ReadSnapshotValues(ReadOnlySpan<byte> payload)
    {
        var values = new float[7];
        for (int index = 0; index < values.Length; ++index)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(
                payload[(8 + index * sizeof(float))..]);
        }
        return values;
    }

    public async ValueTask DisposeAsync()
    {
        _requestGate.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class BotEngineException : Exception
{
    public BotEngineException(
        BotEngineProtocol.MessageType messageType,
        BotEngineProtocol.Status status)
        : base($"Bot Engine Host recusou {messageType}: {status}.")
    {
        MessageType = messageType;
        Status = status;
    }

    public BotEngineProtocol.MessageType MessageType { get; }
    public BotEngineProtocol.Status Status { get; }
}
