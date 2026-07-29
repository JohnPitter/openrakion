using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using RakionServer.Common;

namespace RakionServer.World.Network;

internal static class HumanMatchCapture
{
    private const int MarkerRefreshMilliseconds = 500;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly string MarkerPath = Path.Combine(
        Path.GetTempPath(), "openrakion-human-match.active");
    private static long _nextRefreshMs;
    private static string _outputPath = "";
    private static StreamWriter? _writer;

    public static void RecordClientFrame(
        ClientSession session,
        ushort opcode,
        byte[] payload) =>
        Record(session, new CapturePayload(
            "TCP", "C2S", opcode, payload, $"opcode=0x{opcode:X4}"));

    public static void RecordServerFrame(
        ClientSession session,
        byte[] plaintext)
    {
        if (plaintext.Length < 2)
            return;
        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(plaintext);
        Record(session, new CapturePayload(
            "TCP",
            "S2C",
            opcode,
            plaintext.AsSpan(2).ToArray(),
            $"opcode=0x{opcode:X4}"));
    }

    public static void RecordFieldMessage(
        ClientSession session,
        ushort messageType,
        byte[] payload) =>
        Record(session, new CapturePayload(
            "TCP",
            "S2C",
            messageType,
            payload,
            $"fieldMessage=0x{messageType:X4}"));

    public static void RecordUdp(
        ClientSession session,
        ushort type,
        byte[] packet) =>
        Record(session, new CapturePayload(
            "UDP",
            "C2S",
            type,
            packet,
            DescribeUdp(packet)));

    private static void Record(ClientSession session, CapturePayload payload)
    {
        if (session.GameInfoId <= 0 || session.FieldId < 0)
            return;
        CaptureEvent value = new(
            DateTimeOffset.UtcNow,
            Environment.TickCount64,
            payload.Channel,
            payload.Direction,
            session.FieldId,
            session.FieldSeat,
            session.Status.ToString(),
            $"0x{payload.Type:X4}",
            payload.Bytes.Length,
            payload.Detail,
            Convert.ToHexString(payload.Bytes));
        Write(value);
    }

    private static void Write(CaptureEvent value)
    {
        lock (Sync)
        {
            RefreshOutput();
            if (_writer == null)
                return;
            _writer.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            _writer.Flush();
        }
    }

    private static void RefreshOutput()
    {
        long now = Environment.TickCount64;
        if (now < _nextRefreshMs)
            return;
        _nextRefreshMs = now + MarkerRefreshMilliseconds;

        string directory = ReadCaptureDirectory();
        string output = directory.Length == 0
            ? ""
            : Path.Combine(directory, "server_match.jsonl");
        if (string.Equals(output, _outputPath, StringComparison.OrdinalIgnoreCase))
            return;

        _writer?.Dispose();
        _writer = null;
        _outputPath = output;
        if (output.Length == 0)
            return;
        Directory.CreateDirectory(directory);
        _writer = new StreamWriter(
            new FileStream(output, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false));
        Log.Info("capture", "captura humano x humano ativa em {0}", output);
    }

    private static string ReadCaptureDirectory()
    {
        try
        {
            if (!File.Exists(MarkerPath))
                return "";
            string directory = File.ReadAllText(MarkerPath).Trim();
            return directory.Length == 0
                ? ""
                : Path.GetFullPath(directory);
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string DescribeUdp(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 2)
            return "datagram truncado";
        if (GameplayActionDatagram.TryParseMove(packet, out GameplayMoveAction move))
            return $"move seq={move.Header.Sequence} seat={move.Header.SourceSlot} " +
                $"pos={move.PositionX}/{move.PositionY}/{move.PositionZ} " +
                $"heading={move.AngleWord} state={move.State} action={move.ActionCode}";
        if (GameplayActionDatagram.TryParseSync(packet, out GameplaySyncAction sync))
            return $"sync seq={sync.Header.Sequence} seat={sync.Header.SourceSlot} " +
                $"life={sync.LifeState} animator={sync.AnimatorValue} " +
                $"control={sync.ControlMode}/{sync.ControlDetail}";
        if (GameplayActionDatagram.TryParseAnimation(
            packet, out GameplayAnimationAction animation))
            return $"animation seq={animation.Header.Sequence} " +
                $"seat={animation.Header.SourceSlot} kind={animation.Kind} " +
                $"args={animation.Argument0:X2}/{animation.Argument1:X2}/" +
                $"{animation.Argument2:X2}";
        if (GameplayPeerDatagramCodec.TryParseEntityEvent(
            packet, out GameplayEntityEvent entityEvent))
            return $"event seq={entityEvent.Sequence} seat={entityEvent.SenderSeat} " +
                $"id=0x{entityEvent.EventId:X8} primary={entityEvent.PrimaryEntitySeat} " +
                $"secondary={entityEvent.SecondaryEntitySeat}";
        return $"datagram type=0x{BinaryPrimitives.ReadUInt16LittleEndian(packet):X4}";
    }

    private sealed record CapturePayload(
        string Channel,
        string Direction,
        ushort Type,
        byte[] Bytes,
        string Detail);

    private sealed record CaptureEvent(
        DateTimeOffset Utc,
        long Tick,
        string Channel,
        string Direction,
        int Field,
        byte Seat,
        string Status,
        string Type,
        int Length,
        string Detail,
        string Hex);
}
