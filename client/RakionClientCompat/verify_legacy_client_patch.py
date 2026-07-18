import hashlib
import re
import struct
import sys
from pathlib import Path


def main() -> None:
    dll_path = Path(sys.argv[1])
    header_path = Path(sys.argv[2])
    image = dll_path.read_bytes()
    header = header_path.read_text(encoding="utf-8")
    entries = [
        tuple(int(value, 16) for value in match)
        for match in re.findall(
            r"\{ 0x([0-9A-F]+), 0x([0-9A-F]+), 0x([0-9A-F]+) \}",
            header,
        )
    ]
    missing = [
        entry for entry in entries if struct.pack("<IBB", *entry) not in image
    ]
    if missing:
        raise SystemExit(
            f"ClientPatch legado diverge: {len(missing)}/{len(entries)} entradas ausentes"
        )
    digest = hashlib.sha256(image).hexdigest().upper()
    print(
        f"ClientPatch legado confirma {len(entries)} entradas golden; SHA-256 {digest}"
    )


if __name__ == "__main__":
    main()
