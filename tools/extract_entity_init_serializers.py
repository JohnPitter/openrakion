import argparse
import contextlib
import io
import re
import struct
import subprocess
from pathlib import Path

try:
    from tools.extract_cell_catalog import parse_creature_list_data
    from tools.xfs_read import decode_block, parse
except ModuleNotFoundError:
    from extract_cell_catalog import parse_creature_list_data
    from xfs_read import decode_block, parse


BASE_ADDRESS = 0x35000000
GET_INIT_SLOT = 0x114
APPLY_INIT_SLOT = 0x118
SET_DEFAULT_PROPERTIES_SLOT = 0x28
EXPORT_RVA_RE = re.compile(
    r"^\s*\[\s*(\d+)\]\s+\+base\[\s*\d+\]\s+([0-9a-fA-F]{8}) Export RVA$")
EXPORT_NAME_RE = re.compile(
    r"^\s*\[\s*(\d+)\]\s+\+base\[\s*\d+\]\s+[0-9a-fA-F]+\s+(.+)$")


def parse_objdump_exports(output: str) -> dict[str, int]:
    rvas: dict[int, int] = {}
    names: dict[int, str] = {}
    for line in output.splitlines():
        rva_match = EXPORT_RVA_RE.match(line)
        if rva_match:
            rvas[int(rva_match.group(1))] = int(rva_match.group(2), 16)
            continue
        name_match = EXPORT_NAME_RE.match(line)
        if name_match and "Export RVA" not in line:
            names[int(name_match.group(1))] = name_match.group(2).strip()
    return {name: rvas[index] for index, name in names.items() if index in rvas}


def read_xfs(path: Path) -> tuple[bytes, list[tuple]]:
    with contextlib.redirect_stdout(io.StringIO()):
        return parse(str(path))


def decode_entry(raw: bytes, entry: tuple) -> bytes:
    name, offset, compressed, uncompressed_size, compressed_size = entry
    payload = raw[offset:offset + compressed_size]
    return decode_block(payload, compressed, uncompressed_size, name)


def active_entity_classes(data_setup_xfs: Path, classes_xfs: Path) -> list[tuple[str, str | None]]:
    data_raw, data_entries = read_xfs(data_setup_xfs)
    creature_entry = next(
        entry for entry in data_entries if entry[0].lower().endswith("creaturelist.txt"))
    ecl_paths = parse_creature_list_data(decode_entry(data_raw, creature_entry))

    class_raw, class_entries = read_xfs(classes_xfs)
    by_name = {entry[0].replace("\\", "/").lower(): entry for entry in class_entries}
    result: list[tuple[str, str | None]] = []
    for ecl_path in ecl_paths:
        normalized = ecl_path.replace("\\", "/").lower()
        entry = by_name.get(normalized)
        if entry is None:
            result.append((ecl_path, None))
            continue
        manifest = decode_entry(class_raw, entry).decode("ascii", errors="replace")
        match = re.search(r"^Class:\s*([A-Za-z_][A-Za-z0-9_]*)\s*$", manifest, re.MULTILINE)
        if match is None:
            raise ValueError(f"classe ausente no manifest: {ecl_path}")
        result.append((ecl_path, match.group(1)))
    return result


def read_u32(memory: bytes, rva: int) -> int:
    if rva < 0 or rva + 4 > len(memory):
        raise ValueError(f"RVA fora do dump: 0x{rva:x}")
    return struct.unpack_from("<I", memory, rva)[0]


def map_pe_image(raw: bytes) -> tuple[int, bytes]:
    if len(raw) < 0x40 or raw[:2] != b"MZ":
        raise ValueError("arquivo nao e uma imagem PE")
    pe_offset = struct.unpack_from("<I", raw, 0x3C)[0]
    if pe_offset + 24 > len(raw) or raw[pe_offset:pe_offset + 4] != b"PE\0\0":
        raise ValueError("cabecalho PE ausente ou truncado")

    coff_offset = pe_offset + 4
    section_count = struct.unpack_from("<H", raw, coff_offset + 2)[0]
    optional_size = struct.unpack_from("<H", raw, coff_offset + 16)[0]
    optional_offset = coff_offset + 20
    if optional_offset + optional_size > len(raw) or optional_size < 64:
        raise ValueError("optional header PE truncado")
    if struct.unpack_from("<H", raw, optional_offset)[0] != 0x10B:
        raise ValueError("somente PE32 e suportado")

    image_base = struct.unpack_from("<I", raw, optional_offset + 28)[0]
    image_size = struct.unpack_from("<I", raw, optional_offset + 56)[0]
    header_size = struct.unpack_from("<I", raw, optional_offset + 60)[0]
    if image_size == 0 or image_size > 0x40000000:
        raise ValueError("SizeOfImage invalido")

    memory = bytearray(image_size)
    copied_headers = min(header_size, len(raw), image_size)
    memory[:copied_headers] = raw[:copied_headers]
    section_offset = optional_offset + optional_size
    if section_offset + section_count * 40 > len(raw):
        raise ValueError("tabela de secoes PE truncada")

    for index in range(section_count):
        entry = section_offset + index * 40
        virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from(
            "<IIII", raw, entry + 8)
        if raw_size == 0:
            continue
        if raw_offset + raw_size > len(raw):
            raise ValueError(f"secao PE {index} truncada")
        copy_size = min(raw_size, virtual_size or raw_size)
        if virtual_address + copy_size > image_size:
            raise ValueError(f"secao PE {index} excede SizeOfImage")
        memory[virtual_address:virtual_address + copy_size] = raw[
            raw_offset:raw_offset + copy_size]
    return image_base, bytes(memory)


def is_vtable(memory: bytes, candidate_va: int,
              base_address: int = BASE_ADDRESS) -> bool:
    rva = candidate_va - base_address
    if rva < 0 or rva + APPLY_INIT_SLOT + 4 > len(memory):
        return False
    get_init = read_u32(memory, rva + GET_INIT_SLOT)
    apply_init = read_u32(memory, rva + APPLY_INIT_SLOT)
    module_end = base_address + len(memory)
    return (base_address <= get_init < module_end and
            base_address <= apply_init < module_end)


def local_vtable_assignments(memory: bytes, function_va: int,
                             base_address: int = BASE_ADDRESS) -> list[int]:
    rva = function_va - base_address
    if rva < 0 or rva >= len(memory):
        return []
    code = memory[rva:min(rva + 128, len(memory))]
    assignments: list[int] = []
    for index in range(len(code) - 6):
        if code[index] != 0xC7 or code[index + 1] not in range(0x00, 0x08):
            continue
        value = struct.unpack_from("<I", code, index + 2)[0]
        if is_vtable(memory, value, base_address):
            assignments.append(value)
    return assignments


def direct_calls(memory: bytes, function_va: int,
                 base_address: int = BASE_ADDRESS) -> list[int]:
    rva = function_va - base_address
    if rva < 0 or rva >= len(memory):
        return []
    code = memory[rva:min(rva + 128, len(memory))]
    calls: list[int] = []
    for index in range(len(code) - 5):
        if code[index] != 0xE8:
            continue
        displacement = struct.unpack_from("<i", code, index + 1)[0]
        target = function_va + index + 5 + displacement
        if base_address <= target < base_address + len(memory):
            calls.append(target)
    return calls


def resolve_vtable(memory: bytes, factory_va: int, base_address: int = BASE_ADDRESS,
                   depth: int = 4, visited: set[int] | None = None) -> int | None:
    if visited is None:
        visited = set()
    if factory_va in visited or depth < 0:
        return None
    visited.add(factory_va)
    assignments = local_vtable_assignments(memory, factory_va, base_address)
    if assignments:
        return assignments[-1]
    resolved = None
    for target in direct_calls(memory, factory_va, base_address):
        candidate = resolve_vtable(memory, target, base_address, depth - 1, visited)
        if candidate is not None:
            resolved = candidate
    return resolved


def resolve_export_factory(memory: bytes, dll_class_rva: int,
                           base_address: int = BASE_ADDRESS) -> tuple[int, int] | None:
    candidates = [read_u32(memory, dll_class_rva - 0x30)]
    descriptor_va = read_u32(memory, dll_class_rva)
    descriptor_rva = descriptor_va - base_address
    if 4 <= descriptor_rva < len(memory):
        candidates.append(read_u32(memory, descriptor_rva - 4))
    for factory_va in candidates:
        vtable_va = resolve_vtable(memory, factory_va, base_address)
        if vtable_va is not None:
            return factory_va, vtable_va
    return None


def find_vtable_by_virtual(memory: bytes, method_va: int, slot: int,
                           base_address: int = BASE_ADDRESS) -> int | None:
    needle = struct.pack("<I", method_va)
    matches = []
    start = 0
    while True:
        position = memory.find(needle, start)
        if position < 0:
            break
        candidate_va = base_address + position - slot
        if is_vtable(memory, candidate_va, base_address):
            matches.append(candidate_va)
        start = position + 1
    unique = list(dict.fromkeys(matches))
    return unique[0] if len(unique) == 1 else None


def build_inventory(classes: list[tuple[str, str | None]], exports: dict[str, int],
                    memory: bytes, base_address: int = BASE_ADDRESS) -> list[dict]:
    names_by_va = {base_address + rva: name for name, rva in exports.items()}
    rows = []
    for ecl_path, class_name in classes:
        if class_name is None:
            rows.append({"ecl": ecl_path, "class": "-", "status": "manifest_missing"})
            continue
        dll_export = f"{class_name}_DLLClass"
        dll_class_rva = exports.get(dll_export)
        if dll_class_rva is None:
            rows.append({"ecl": ecl_path, "class": class_name, "status": "export_missing"})
            continue
        set_defaults = exports.get(f"?SetDefaultProperties@{class_name}@@UAEXXZ")
        vtable_va = None
        if set_defaults is not None:
            vtable_va = find_vtable_by_virtual(
                memory, base_address + set_defaults, SET_DEFAULT_PROPERTIES_SLOT,
                base_address)
        resolved = resolve_export_factory(memory, dll_class_rva, base_address)
        if vtable_va is not None:
            factory_va = (resolved[0] if resolved is not None and
                          resolved[1] == vtable_va else None)
            resolved = factory_va, vtable_va
        if resolved is None:
            factory_va = read_u32(memory, dll_class_rva - 0x30)
            rows.append({
                "ecl": ecl_path, "class": class_name, "factory": factory_va,
                "status": "vtable_unresolved",
            })
            continue
        factory_va, vtable_va = resolved
        get_init_va = read_u32(memory, vtable_va - base_address + GET_INIT_SLOT)
        apply_init_va = read_u32(memory, vtable_va - base_address + APPLY_INIT_SLOT)
        rows.append({
            "ecl": ecl_path,
            "class": class_name,
            "factory": factory_va,
            "vtable": vtable_va,
            "get_init": get_init_va,
            "get_init_name": names_by_va.get(get_init_va, "-"),
            "apply_init": apply_init_va,
            "apply_init_name": names_by_va.get(apply_init_va, "-"),
            "status": "ok",
        })
    return rows


def format_tsv(rows: list[dict]) -> str:
    lines = ["ecl\tclass\tstatus\tfactory\tvtable\tget_init\tapply_init\tfamily"]
    families: dict[tuple[int, int], int] = {}
    for row in rows:
        if row["status"] != "ok":
            lines.append("\t".join((
                row["ecl"], row["class"], row["status"],
                f"0x{row['factory']:08X}" if "factory" in row else "-",
                "-", "-", "-", "-",
            )))
            continue
        key = (row["get_init"], row["apply_init"])
        family = families.setdefault(key, len(families) + 1)
        lines.append("\t".join((
            row["ecl"], row["class"], row["status"],
            f"0x{row['factory']:08X}" if row["factory"] is not None else "-",
            f"0x{row['vtable']:08X}",
            f"0x{row['get_init']:08X} {row['get_init_name']}",
            f"0x{row['apply_init']:08X} {row['apply_init_name']}", f"F{family:02d}",
        )))
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Agrupa as criaturas ativas pelos serializers virtuais de init do entitiesmp")
    parser.add_argument("--module", type=Path, required=True)
    parser.add_argument(
        "--memory-dump", type=Path,
        help="imagem virtual opcional; por padrao o PE de --module e mapeado localmente")
    parser.add_argument("--data-setup-xfs", type=Path, required=True)
    parser.add_argument("--classes-xfs", type=Path, required=True)
    parser.add_argument("--objdump", default="objdump")
    args = parser.parse_args()

    output = subprocess.run(
        [args.objdump, "-p", str(args.module)], check=True, capture_output=True,
        text=True, encoding="utf-8", errors="replace").stdout
    exports = parse_objdump_exports(output)
    classes = active_entity_classes(args.data_setup_xfs, args.classes_xfs)
    if args.memory_dump is None:
        base_address, memory = map_pe_image(args.module.read_bytes())
    else:
        base_address, memory = BASE_ADDRESS, args.memory_dump.read_bytes()
    rows = build_inventory(classes, exports, memory, base_address)
    print(format_tsv(rows))


if __name__ == "__main__":
    main()
