import struct
import sys
import unittest
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from xfs_read import decode_block


class XfsReadTests(unittest.TestCase):
    def test_decodes_full_zlib_block_after_eight_byte_header(self):
        plain = b"stage-data" * 20
        compressed = zlib.compress(plain)
        block = struct.pack("<HB3sH", len(plain), 0x80,
                            len(compressed).to_bytes(3, "little"), 0) + compressed

        self.assertEqual(plain, decode_block(block, 1, len(plain)))

    def test_decodes_legacy_small_block_without_adler_trailer(self):
        plain = bytes.fromhex("2F2F20C1BEB7E1")
        compressed = zlib.compress(plain)[:-4]
        block = len(compressed).to_bytes(3, "little") + b"\x00\x00" + compressed

        self.assertEqual(plain, decode_block(block, 1, len(plain)))

    def test_decodes_multiple_sixty_four_kib_blocks(self):
        chunks = (b"A" * 65_536, b"tail" * 24)
        blocks = []
        for chunk in chunks:
            compressed = zlib.compress(chunk)
            encoded_size = 0 if len(chunk) == 65_536 else len(chunk)
            blocks.append(struct.pack("<HB3sH", encoded_size, 0x80,
                                      len(compressed).to_bytes(3, "little"), 0) + compressed)

        self.assertEqual(b"".join(chunks),
                         decode_block(b"".join(blocks), 1, sum(map(len, chunks))))


if __name__ == "__main__":
    unittest.main()
