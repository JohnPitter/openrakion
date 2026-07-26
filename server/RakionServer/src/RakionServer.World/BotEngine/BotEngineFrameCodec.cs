using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RakionServer.World.BotEngine;

internal static class BotEngineFrameCodec
{
    public static byte[] EncodeRequest(
        BotEngineProtocol.MessageType type,
        uint correlationId,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length > BotEngineProtocol.MaximumPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(payload));

        byte[] frame = new byte[BotEngineProtocol.HeaderSize + payload.Length];
        Span<byte> header = frame.AsSpan(0, BotEngineProtocol.HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header, BotEngineProtocol.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], BotEngineProtocol.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)type);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], correlationId);
        payload.CopyTo(frame.AsSpan(BotEngineProtocol.HeaderSize));
        return frame;
    }

    public static byte[] EncodeLoadField(
        uint fieldId,
        ushort maximumBots,
        byte mapId,
        byte mode,
        string worldName)
    {
        byte[] world = EncodeWorldName(worldName);
        if (fieldId == 0 || maximumBots == 0 ||
            mapId is < 200 or > 213 || mode is < 1 or > 4 ||
            world.Length == 0 || world.Length >= BotEngineProtocol.WorldNameCapacity)
            throw new ArgumentException("LoadField possui valores inválidos.");

        byte[] payload = new byte[BotEngineProtocol.LoadFieldRequestSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, fieldId);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), maximumBots);
        payload[6] = mapId;
        payload[7] = mode;
        world.CopyTo(payload.AsSpan(8));
        return payload;
    }

    public static byte[] EncodeAddBot(BotEngineBotRequest request)
    {
        byte[] name = EncodePlayerText(request.Name, 32, nameof(request.Name));
        byte[] species = EncodePlayerText(
            request.Species, 16, nameof(request.Species));
        if (request.BotId == 0)
            throw new ArgumentException("BotId inválido.", nameof(request));

        byte[] payload = new byte[BotEngineProtocol.AddBotRequestSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, request.BotId);
        name.CopyTo(payload.AsSpan(4));
        species.CopyTo(payload.AsSpan(36));
        return payload;
    }

    private static byte[] EncodeWorldName(string worldName)
    {
        if (!worldName.StartsWith(@"LevelsSV\", StringComparison.Ordinal) ||
            !worldName.EndsWith(".wld", StringComparison.OrdinalIgnoreCase) ||
            worldName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Caminho de mundo inválido.", nameof(worldName));
        foreach (char character in worldName)
        {
            if (character > 127)
                throw new ArgumentException(
                    "Caminho de mundo deve usar ASCII.", nameof(worldName));
        }
        return Encoding.ASCII.GetBytes(worldName);
    }

    private static byte[] EncodePlayerText(
        string value,
        int capacity,
        string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(
                character => character is < ' ' or > '~'))
            throw new ArgumentException("Texto de player inválido.", parameter);
        byte[] encoded = Encoding.ASCII.GetBytes(value);
        if (encoded.Length >= capacity)
            throw new ArgumentException("Texto de player excede o contrato.", parameter);
        return encoded;
    }

    public static async ValueTask<BotEngineFrame> ReadResponseAsync(
        Stream stream,
        BotEngineProtocol.MessageType expectedType,
        uint expectedCorrelation,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[BotEngineProtocol.HeaderSize];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        ValidateResponseHeader(header, expectedType, expectedCorrelation);

        int payloadLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            header.AsSpan(8)));
        byte[] payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return new BotEngineFrame(
            expectedType,
            expectedCorrelation,
            (BotEngineProtocol.Status)BinaryPrimitives.ReadUInt32LittleEndian(
                header.AsSpan(16)),
            payload);
    }

    private static void ValidateResponseHeader(
        ReadOnlySpan<byte> header,
        BotEngineProtocol.MessageType expectedType,
        uint expectedCorrelation)
    {
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        uint correlation = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (magic != BotEngineProtocol.Magic ||
            version != BotEngineProtocol.Version ||
            type != ((ushort)expectedType | BotEngineProtocol.ResponseFlag) ||
            correlation != expectedCorrelation ||
            payloadLength > BotEngineProtocol.MaximumPayloadSize)
            throw new InvalidDataException("Resposta inválida do Bot Engine Host.");
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException(
                    "Bot Engine Host encerrou o pipe durante um frame.");
            offset += read;
        }
    }
}
