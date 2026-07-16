#!/usr/bin/env python3
"""Extrai os streams zlib do NyxLauncherEnc.xfs sem alterar o arquivo original."""

from __future__ import annotations

import argparse
import zlib
from pathlib import Path


def extract_streams(payload: bytes) -> list[tuple[int, bytes]]:
    streams: list[tuple[int, bytes]] = []
    offset = 0
    while True:
        offset = payload.find(b"\x78\x9c", offset)
        if offset < 0:
            return streams
        try:
            streams.append((offset, zlib.decompress(payload[offset:])))
        except zlib.error:
            pass
        offset += 2


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("xfs", type=Path)
    args = parser.parse_args()
    for offset, content in extract_streams(args.xfs.read_bytes()):
        print(f"=== zlib 0x{offset:X} ({len(content)} bytes) ===")
        print(content.decode("latin-1", errors="replace"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
