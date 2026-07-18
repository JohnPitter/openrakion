import hashlib
import json
import struct
import sys
from pathlib import Path


VERSION_EXPORTS = {
    "GetFileVersionInfoA",
    "GetFileVersionInfoByHandle",
    "GetFileVersionInfoExA",
    "GetFileVersionInfoExW",
    "GetFileVersionInfoSizeA",
    "GetFileVersionInfoSizeExA",
    "GetFileVersionInfoSizeExW",
    "GetFileVersionInfoSizeW",
    "GetFileVersionInfoW",
    "VerFindFileA",
    "VerFindFileW",
    "VerInstallFileA",
    "VerInstallFileW",
    "VerLanguageNameA",
    "VerLanguageNameW",
    "VerQueryValueA",
    "VerQueryValueW",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def pe_layout(image: bytes) -> tuple[int, list[tuple[int, int, int, int]]]:
    pe = struct.unpack_from("<I", image, 0x3C)[0]
    if image[pe : pe + 4] != b"PE\0\0":
        raise ValueError("cabeçalho PE inválido")
    count = struct.unpack_from("<H", image, pe + 6)[0]
    optional_size = struct.unpack_from("<H", image, pe + 20)[0]
    sections = []
    offset = pe + 24 + optional_size
    for index in range(count):
        entry = offset + index * 40
        virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from(
            "<IIII", image, entry + 8
        )
        sections.append((virtual_address, virtual_size, raw_offset, raw_size))
    return pe, sections


def rva_offset(sections: list[tuple[int, int, int, int]], rva: int) -> int:
    for virtual, virtual_size, raw, raw_size in sections:
        if virtual <= rva < virtual + max(virtual_size, raw_size):
            return raw + rva - virtual
    raise ValueError(f"RVA 0x{rva:X} fora das seções")


def read_c_string(image: bytes, offset: int) -> str:
    end = image.index(0, offset)
    return image[offset:end].decode("ascii")


def imports(path: Path) -> set[str]:
    image = path.read_bytes()
    pe, sections = pe_layout(image)
    optional = pe + 24
    magic = struct.unpack_from("<H", image, optional)[0]
    directory = optional + (96 if magic == 0x10B else 112)
    import_rva = struct.unpack_from("<I", image, directory + 8)[0]
    offset = rva_offset(sections, import_rva)
    names = set()
    while True:
        descriptor = struct.unpack_from("<IIIII", image, offset)
        if not any(descriptor):
            return names
        names.add(read_c_string(image, rva_offset(sections, descriptor[3])).upper())
        offset += 20


def exports(path: Path) -> set[str]:
    image = path.read_bytes()
    pe, sections = pe_layout(image)
    optional = pe + 24
    magic = struct.unpack_from("<H", image, optional)[0]
    directory = optional + (96 if magic == 0x10B else 112)
    export_rva = struct.unpack_from("<I", image, directory)[0]
    export_offset = rva_offset(sections, export_rva)
    name_count = struct.unpack_from("<I", image, export_offset + 24)[0]
    names_rva = struct.unpack_from("<I", image, export_offset + 32)[0]
    names_offset = rva_offset(sections, names_rva)
    result = set()
    for index in range(name_count):
        name_rva = struct.unpack_from("<I", image, names_offset + index * 4)[0]
        result.add(read_c_string(image, rva_offset(sections, name_rva)))
    return result


def main() -> None:
    root = Path(sys.argv[1]).resolve()
    manifest = json.loads((root / "validation-install.json").read_text(encoding="utf-8"))
    failures = []
    for relative, expected in manifest["files"].items():
        path = root / relative
        if not path.is_file():
            failures.append(f"ausente: {relative}")
        elif sha256(path) != expected:
            failures.append(f"hash divergente: {relative}")

    executable = root / "Bin" / "rakion.exe"
    engine = root / "Bin" / "engine.dll"
    proxy = root / "Bin" / "version.dll"
    client_patch = root / "Bin" / "RakionClientPatch.dll"
    legacy_forwarder = root / "Bin" / "verorig.dll"
    if sha256(executable) != manifest["baseline"]["rakionExeOriginalSha256"]:
        failures.append("rakion.exe não é o baseline pristine")
    if sha256(engine) != manifest["baseline"]["engineSha256"]:
        failures.append("engine.dll não é a golden v258")
    if "VERSION.DLL" not in imports(executable):
        failures.append("rakion.exe não importa version.dll")
    if legacy_forwarder.exists():
        failures.append("verorig.dll legado ainda está instalado")
    if not client_patch.is_file():
        failures.append("RakionClientPatch.dll ausente")
    missing_exports = VERSION_EXPORTS - exports(proxy)
    if missing_exports:
        failures.append("exports ausentes no proxy: " + ", ".join(sorted(missing_exports)))
    if failures:
        raise SystemExit("instalação inválida:\n- " + "\n- ".join(failures))
    print(f"instalação v258 íntegra: {len(manifest['files'])} arquivos; proxy com 17 exports")


if __name__ == "__main__":
    main()
