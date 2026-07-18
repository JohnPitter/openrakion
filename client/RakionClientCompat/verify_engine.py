import hashlib
import struct
import sys
from pathlib import Path


EXPECTED_SHA256 = "83b20d6c32cd66b95c8f8e41ad6de13a58e8f5f948cd21cbd118d42ef8cf88f2"
SEND_TO_OTHER_CLIENT_RVA = 0x100780
EXPECTED_PROLOGUE = bytes.fromhex("83 ec 0c 55 8b e9")


def rva_to_offset(image: bytes, rva: int) -> int:
    pe_offset = struct.unpack_from("<I", image, 0x3C)[0]
    section_count = struct.unpack_from("<H", image, pe_offset + 6)[0]
    optional_size = struct.unpack_from("<H", image, pe_offset + 20)[0]
    section_offset = pe_offset + 24 + optional_size
    for index in range(section_count):
        entry = section_offset + index * 40
        virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from(
            "<IIII", image, entry + 8
        )
        if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
            return raw_offset + rva - virtual_address
    raise ValueError(f"RVA 0x{rva:X} não pertence a uma seção")


def main() -> None:
    path = Path(sys.argv[1])
    image = path.read_bytes()
    digest = hashlib.sha256(image).hexdigest()
    if digest != EXPECTED_SHA256:
        raise SystemExit(f"engine.dll incompatível: SHA-256 {digest}")
    offset = rva_to_offset(image, SEND_TO_OTHER_CLIENT_RVA)
    actual = image[offset : offset + len(EXPECTED_PROLOGUE)]
    if actual != EXPECTED_PROLOGUE:
        raise SystemExit(
            f"prólogo SendToOtherClient incompatível: {actual.hex(' ')}"
        )
    print("engine.dll v258 e prólogo SendToOtherClient verificados")


if __name__ == "__main__":
    main()
