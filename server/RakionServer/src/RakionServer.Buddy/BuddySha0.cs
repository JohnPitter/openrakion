using System;
using System.Buffers.Binary;

namespace RakionServer.Buddy
{
    internal static class BuddySha0
    {
        public static byte[] ComputeRawState(ReadOnlySpan<byte> input)
        {
            uint[] state =
            [
                0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476, 0xc3d2e1f0
            ];
            byte[] padded = Pad(input);
            var words = new uint[80];
            for (int offset = 0; offset < padded.Length; offset += 64)
                Transform(padded.AsSpan(offset, 64), state, words);

            byte[] digest = new byte[20];
            for (int index = 0; index < state.Length; index++)
                BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(index * 4), state[index]);
            return digest;
        }

        private static byte[] Pad(ReadOnlySpan<byte> input)
        {
            int paddedLength = checked(((input.Length + 9 + 63) / 64) * 64);
            byte[] padded = new byte[paddedLength];
            input.CopyTo(padded);
            padded[input.Length] = 0x80;
            BinaryPrimitives.WriteUInt64BigEndian(
                padded.AsSpan(paddedLength - 8), checked((ulong)input.Length * 8));
            return padded;
        }

        private static void Transform(ReadOnlySpan<byte> block, uint[] state, uint[] words)
        {
            for (int index = 0; index < 16; index++)
                words[index] = BinaryPrimitives.ReadUInt32BigEndian(block[(index * 4)..]);
            for (int index = 16; index < words.Length; index++)
                words[index] = words[index - 3] ^ words[index - 8] ^
                    words[index - 14] ^ words[index - 16];

            uint a = state[0], b = state[1], c = state[2], d = state[3], e = state[4];
            for (int index = 0; index < words.Length; index++)
            {
                (uint function, uint constant) = Round(index, b, c, d);
                uint next = RotateLeft(a, 5) + function + e + constant + words[index];
                (a, b, c, d, e) = (next, a, RotateLeft(b, 30), c, d);
            }
            state[0] += a;
            state[1] += b;
            state[2] += c;
            state[3] += d;
            state[4] += e;
        }

        private static (uint Function, uint Constant) Round(
            int index, uint b, uint c, uint d) => index switch
        {
            < 20 => ((b & c) | (~b & d), 0x5a827999),
            < 40 => (b ^ c ^ d, 0x6ed9eba1),
            < 60 => ((b & c) | (b & d) | (c & d), 0x8f1bbcdc),
            _ => (b ^ c ^ d, 0xca62c1d6)
        };

        private static uint RotateLeft(uint value, int bits) =>
            (value << bits) | (value >> (32 - bits));
    }
}
