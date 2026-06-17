using System;
using System.Collections.Generic;
using System.IO;
using RakionServer.World.CharSelect;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden test do 0x0C SINTETIZADO: reproduz os estados que plantei no servidor ORIGINAL (captura-diff,
    /// memória 0c-login-frame-format) e exige bytes idênticos às fixtures capturadas, exceto os campos de
    /// sessão (@7..12, que variam por login) e o trailer. Prova que a síntese de raiz reproduz o frame real.
    /// </summary>
    public class CharListWriterGoldenTests
    {
        private const int HeaderSize = 65;
        private const int RecordFixed = 365;   // 5(marker+pad) + 1(\0) + 359(fields)

        private static byte[] Fixture(string name) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));

        private static CharSummary Abcdef() => new()
        {
            Name = "ABCDEF", Slot = 0, Level = 119, LevelPoint = 86,
            Win = 0x0A0B0C0D, Lose = 0x1A1B1C1D, Draw = 0x2A2B2C2D, Exp = 0x11223344,
            Stats = new ushort[] { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 },
            StageRanks = new byte[] { 5, 4, 3, 2, 1 },
        };

        private static CharSummary Ghijklmn() => new()
        {
            Name = "GHIJKLMN", Slot = 1, Level = 85, LevelPoint = 99,
            Win = 0x4A4B4C4D, Lose = 0x5A5B5C5D, Draw = 0x6A6B6C6D, Exp = 0x7A7B7C7D,
            Stats = new ushort[] { 121, 122, 123, 124, 125, 126, 127, 120, 119, 118 },
            StageRanks = new byte[] { 1, 2, 3, 4, 5 },
        };

        [Fact]
        public void Synthesizes_OneChar_LikeOriginal()
        {
            var list = new CharList
            {
                AccountName = "JP", Gold = 0x3A3B3C3D, PowerLevelPoint = 0x4A4B,
                SessionHandle = new byte[] { 0xa0, 0x0d, 0x87, 0x3f },
                Chars = new List<CharSummary> { Abcdef() },
            };
            AssertMatches(LoginCharListWriter.Build(list), Fixture("golden_0c_1char.bin"), list);
        }

        [Fact]
        public void Synthesizes_TwoChars_LikeOriginal()
        {
            var list = new CharList
            {
                AccountName = "JP", Gold = 0x3A3B3C3D, PowerLevelPoint = 0x4A4B,
                SessionHandle = new byte[] { 0xa9, 0x0d, 0x87, 0x3f },
                Chars = new List<CharSummary> { Abcdef(), Ghijklmn() },
            };
            AssertMatches(LoginCharListWriter.Build(list), Fixture("golden_0c_2char.bin"), list);
        }

        [Fact]
        public void Synthesizes_TwoChars_WithClass_LikeOriginal()
        {
            var list = new CharList
            {
                AccountName = "JP", Gold = 0x3A3B3C3D, PowerLevelPoint = 0x4A4B,
                SessionHandle = new byte[] { 0xd7, 0x0d, 0x87, 0x3f },
                Chars = new List<CharSummary> { Abcdef() with { Class = 3 }, Ghijklmn() with { Class = 4 } },
            };
            AssertMatches(LoginCharListWriter.Build(list), Fixture("golden_0c_2char_cls.bin"), list);
        }

        // header + records byte-a-byte; @7..12 (sessão) mascarado; trailer ignorado (varia por login).
        private static void AssertMatches(byte[] synth, byte[] fixture, CharList list)
        {
            int compareLen = HeaderSize;
            foreach (var c in list.Chars) compareLen += c.Name.Length + RecordFixed;
            Assert.True(fixture.Length >= compareLen, $"fixture {fixture.Length}B < esperado {compareLen}B");
            for (int i = 0; i < compareLen; i++)
            {
                if (i is >= 7 and <= 12) continue;   // sessão/seq — varia por login
                Assert.True(synth[i] == fixture[i], $"divergência @{i}: synth={synth[i]:x2} fixture={fixture[i]:x2}");
            }
        }
    }
}
