using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RakionServer.OracleDiff;

internal static class Program
{
    private const string TcpPrefix = "TCP";
    private const string UdpPrefix = "UDP";
    private const string DirectionOption = "--direction";
    private const string TransportOption = "--transport";
    private const int Success = 0;
    private const int Failure = 1;

    private static readonly Regex TcpLine = new(
        @"(C->W|W->C)\s+size=(\d+)\s+u16a=0x([0-9a-fA-F]+)\s+u16b=0x([0-9a-fA-F]+)\s+len=(\d+)\s+data=([0-9a-fA-F]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UdpLine = new(
        @"(C->W|W->C)\s+UDP:(\d+)\s+(\d+)B\s+([0-9a-fA-F]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
            return SelfTest();

        if (args.Length == 2 && args[0] == "summarize")
            return Summarize(args[1]);

        if (args.Length >= 3 && args[0] == "compare")
            return Compare(args);

        PrintUsage();
        return Failure;
    }

    private static int Summarize(string logPath)
    {
        var frames = OracleLogParser.ParseFile(logPath);
        Console.WriteLine($"frames={frames.Count}");

        foreach (var group in frames.GroupBy(f => f.SummaryKey).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"{group.Key} x{group.Count()}");

        return Success;
    }

    private static int Compare(string[] args)
    {
        var options = CompareOptions.Parse(args.Skip(3).ToArray());
        var expected = options.Apply(OracleLogParser.ParseFile(args[1]));
        var actual = options.Apply(OracleLogParser.ParseFile(args[2]));
        var result = OracleComparer.Compare(expected, actual);

        if (result.Match)
        {
            Console.WriteLine($"match frames={expected.Count}");
            return Success;
        }

        Console.WriteLine(result.Message);
        return Failure;
    }

    private static int SelfTest()
    {
        const string sample =
            "C->W size=34   u16a=0x000c u16b=0x0000 len=24 data=0c00000000440074657374007465737400010000df69b663\n" +
            "W->C size=18   u16a=0x0048 u16b=0x0001 len=12 data=480001b3010000141400a00f\n" +
            "C->W UDP:41709 11B 0040050000000004000000\n" +
            "W->C UDP:41709 8B 158304000000000a\n";

        var frames = OracleLogParser.ParseText(sample);
        Require(frames.Count == 4, "parser should read all frames");
        Require(frames[0].Transport == TcpPrefix, "first frame should be TCP");
        Require(frames[1].MessageType == 0x48, "second frame should expose msgType 0x48");
        Require(frames[2].Transport == UdpPrefix, "third frame should be UDP");
        Require(frames[3].Payload[7] == 0x0a, "1583 state byte should be preserved");

        var changed = frames.ToArray();
        changed[3] = changed[3] with { Payload = Hex.Decode("1583040000000007") };
        var diff = OracleComparer.Compare(frames, changed);
        Require(!diff.Match && diff.Message.Contains("payload mismatch", StringComparison.Ordinal), "comparer should catch byte diff");

        var filtered = CompareOptions.Parse(new[] { "--direction", "W->C", "--transport", "UDP" }).Apply(frames);
        Require(filtered.Count == 1 && filtered[0].Payload[0] == 0x15, "compare filters should keep only selected frames");

        Console.WriteLine("self-test ok");
        return Success;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("usage:");
        Console.WriteLine("  RakionServer.OracleDiff --self-test");
        Console.WriteLine("  RakionServer.OracleDiff summarize <mitm.log>");
        Console.WriteLine("  RakionServer.OracleDiff compare <oracle.log> <actual.log> [--direction C->W|W->C] [--transport TCP|UDP]");
    }

    private sealed record OracleFrame(
        int Index,
        string Transport,
        string Direction,
        int Size,
        int? Port,
        ushort? U16A,
        ushort? U16B,
        byte[] Payload)
    {
        public ushort MessageType => Payload.Length >= 2
            ? (ushort)(Payload[0] | (Payload[1] << 8))
            : (ushort)0;

        public string SummaryKey
        {
            get
            {
                if (Transport == TcpPrefix)
                    return $"{Transport}:{Direction}:msg=0x{MessageType:x4}:len={Payload.Length}";

                string marker = Payload.Length >= 2 ? $"{Payload[0]:x2}{Payload[1]:x2}" : "short";
                return $"{Transport}:{Direction}:marker={marker}:len={Payload.Length}";
            }
        }
    }

    private sealed record CompareOptions(string? Direction, string? Transport)
    {
        public static CompareOptions Parse(string[] args)
        {
            string? direction = null;
            string? transport = null;

            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i];
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"missing value for {option}");

                string value = args[++i];
                if (option == DirectionOption)
                    direction = value;
                else if (option == TransportOption)
                    transport = value.ToUpperInvariant();
                else
                    throw new ArgumentException($"unknown option: {option}");
            }

            return new CompareOptions(direction, transport);
        }

        public IReadOnlyList<OracleFrame> Apply(IReadOnlyList<OracleFrame> frames)
        {
            IEnumerable<OracleFrame> query = frames;

            if (!string.IsNullOrEmpty(Direction))
                query = query.Where(f => f.Direction == Direction);

            if (!string.IsNullOrEmpty(Transport))
                query = query.Where(f => f.Transport == Transport);

            return query.ToArray();
        }
    }

    private static class OracleLogParser
    {
        public static IReadOnlyList<OracleFrame> ParseFile(string logPath)
        {
            if (!File.Exists(logPath))
                throw new FileNotFoundException("log not found", logPath);

            return ParseLines(File.ReadLines(logPath));
        }

        public static IReadOnlyList<OracleFrame> ParseText(string text)
            => ParseLines(text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));

        private static IReadOnlyList<OracleFrame> ParseLines(IEnumerable<string> lines)
        {
            var frames = new List<OracleFrame>();

            foreach (string line in lines)
            {
                var tcp = TcpLine.Match(line);
                if (tcp.Success)
                {
                    frames.Add(ParseTcp(frames.Count, tcp));
                    continue;
                }

                var udp = UdpLine.Match(line);
                if (udp.Success)
                    frames.Add(ParseUdp(frames.Count, udp));
            }

            return frames;
        }

        private static OracleFrame ParseTcp(int index, Match match)
        {
            string direction = match.Groups[1].Value;
            int size = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            ushort u16a = ParseUInt16Hex(match.Groups[3].Value);
            ushort u16b = ParseUInt16Hex(match.Groups[4].Value);
            int length = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
            byte[] payload = Hex.Decode(match.Groups[6].Value);

            if (payload.Length != length)
                throw new InvalidDataException($"TCP frame {index} length mismatch: declared={length} actual={payload.Length}");

            return new OracleFrame(index, TcpPrefix, direction, size, null, u16a, u16b, payload);
        }

        private static OracleFrame ParseUdp(int index, Match match)
        {
            string direction = match.Groups[1].Value;
            int port = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            int length = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            byte[] payload = Hex.Decode(match.Groups[4].Value);

            if (payload.Length != length)
                throw new InvalidDataException($"UDP frame {index} length mismatch: declared={length} actual={payload.Length}");

            return new OracleFrame(index, UdpPrefix, direction, length, port, null, null, payload);
        }

        private static ushort ParseUInt16Hex(string value)
            => ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static class OracleComparer
    {
        public static CompareResult Compare(IReadOnlyList<OracleFrame> expected, IReadOnlyList<OracleFrame> actual)
        {
            int count = Math.Min(expected.Count, actual.Count);

            for (int i = 0; i < count; i++)
            {
                var left = expected[i];
                var right = actual[i];

                if (left.Transport != right.Transport || left.Direction != right.Direction)
                    return CompareResult.Fail($"frame {i}: route mismatch expected={left.Transport}:{left.Direction} actual={right.Transport}:{right.Direction}");

                if (left.Payload.Length != right.Payload.Length)
                    return CompareResult.Fail($"frame {i}: payload length mismatch expected={left.Payload.Length} actual={right.Payload.Length}");

                int diffAt = FirstPayloadDiff(left.Payload, right.Payload);
                if (diffAt >= 0)
                {
                    return CompareResult.Fail(
                        $"frame {i}: payload mismatch at +0x{diffAt:x} expected=0x{left.Payload[diffAt]:x2} actual=0x{right.Payload[diffAt]:x2} " +
                        $"key={left.SummaryKey}");
                }
            }

            if (expected.Count != actual.Count)
                return CompareResult.Fail($"frame count mismatch expected={expected.Count} actual={actual.Count}");

            return CompareResult.Ok();
        }

        private static int FirstPayloadDiff(byte[] expected, byte[] actual)
        {
            for (int i = 0; i < expected.Length; i++)
                if (expected[i] != actual[i])
                    return i;

            return -1;
        }
    }

    private readonly record struct CompareResult(bool Match, string Message)
    {
        public static CompareResult Ok() => new(true, "");
        public static CompareResult Fail(string message) => new(false, message);
    }

    private static class Hex
    {
        public static byte[] Decode(string value)
        {
            if ((value.Length & 1) != 0)
                throw new FormatException("hex string must have an even length");

            byte[] bytes = new byte[value.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(value.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            return bytes;
        }
    }
}
