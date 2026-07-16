import zlib
import unittest
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from extract_nyx_config import extract_streams


class ExtractNyxConfigTests(unittest.TestCase):
    def test_extracts_all_valid_streams_and_ignores_false_header(self) -> None:
        first = zlib.compress(b"[NyxLauncher]\r\nUrl_Fetch=http://localhost/fetch.php\r\n")
        second = zlib.compress(b"XFS2")
        payload = b"head\x78\x9cbad" + first + b"gap" + second

        streams = extract_streams(payload)

        self.assertEqual(
            [content for _, content in streams],
            [zlib.decompress(first), b"XFS2"],
        )
