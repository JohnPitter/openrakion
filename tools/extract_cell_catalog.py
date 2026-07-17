import argparse
import contextlib
import hashlib
import io
import json
import re
import struct
from pathlib import Path


ITEM_BASE = 8000
CREATURE_RECORD_SIZE = 8118
NPC_LEVEL_COUNT = 99
SECONDARY_RECORDS_PER_CREATURE = 4
SECONDARY_RECORD_SIZE = 33

# Ordem exata consumida por CPlayer_ReadNpcDataCore @ 0x35228D10. Os nomes de
# domínio só são atribuídos quando há consumidor comprovado; os demais mantêm
# o offset runtime como identidade estável.
NPC_LEVEL_FIELDS = (
    ("unknown_runtime_00", 0x0000, 0x00, "I", None),
    ("cumulative_cell_exp", 0x018C, 0x08, "I", None),
    ("attack", 0x0318, 0x14, "H", 466),
    ("armor", 0x03DE, 0x20, "H", 467),
    ("energy", 0x04A4, 0x2C, "H", 468),
    ("unknown_runtime_34", 0x056A, 0x34, "f", None),
    ("speed", 0x06F6, 0x38, "f", 469),
    ("unknown_runtime_3c", 0x0882, 0x3C, "f", None),
    ("attack_speed", 0x0A0E, 0x40, "f", 470),
    ("vision_range", 0x0B9A, 0x44, "f", 471),
    ("distance_speed", 0x0D26, 0x48, "f", 472),
    ("unknown_runtime_4c", 0x0EB2, 0x4C, "f", None),
    ("distance_attack_speed", 0x103E, 0x50, "f", 473),
    ("unknown_runtime_54", 0x11CA, 0x54, "f", None),
    ("recovery_time", 0x1356, 0x58, "f", 474),
    ("unknown_runtime_5c", 0x14E2, 0x5C, "f", None),
    ("npc_kill_cp_reward", 0x166E, 0x64, "H", None),
    ("summon_cp_cost", 0x1734, 0x70, "H", 475),
    ("unknown_runtime_7c", 0x17FA, 0x7C, "H", None),
    ("upgrade_gold", 0x18C0, 0x84, "f", None),
    ("unconsumed_field_1a4c", 0x1A4C, 0x8C, "I", None),
    ("unknown_runtime_94", 0x1BD8, 0x94, "H", None),
    ("unknown_runtime_98", 0x1C9E, 0x98, "I", None),
    ("unknown_runtime_9c", 0x1E2A, 0x9C, "I", None),
)

NPC_LEVEL_FIELDS_BY_NAME = {field[0]: field for field in NPC_LEVEL_FIELDS}
CUMULATIVE_EXP_OFFSET = NPC_LEVEL_FIELDS_BY_NAME["cumulative_cell_exp"][1]
NPC_KILL_CP_REWARD_OFFSET = NPC_LEVEL_FIELDS_BY_NAME["npc_kill_cp_reward"][1]
SUMMON_CP_COST_OFFSET = NPC_LEVEL_FIELDS_BY_NAME["summon_cp_cost"][1]
UNCONSUMED_FIELD_1A4C_OFFSET = NPC_LEVEL_FIELDS_BY_NAME["unconsumed_field_1a4c"][1]


def parse_creature_list_data(data: bytes) -> list[str]:
    entries = []
    for raw_line in data.decode("latin1").splitlines():
        line = raw_line.strip()
        if line and not line.startswith("//"):
            entries.append(line.replace("\\", "/"))
    return entries


def parse_creature_list(path: Path) -> list[str]:
    return parse_creature_list_data(path.read_bytes())


def read_c_string(data: bytes, offset: int, limit: int) -> tuple[str, int]:
    end = data.find(b"\0", offset, min(len(data), offset + limit + 1))
    if end < 0:
        raise ValueError(f"string sem terminador em 0x{offset:x}")
    return data[offset:end].decode("latin1"), end + 1


def parse_item_record(data: bytes, item_id: int) -> dict | None:
    encoded_id = struct.pack("<I", item_id)
    marker = encoded_id + b"\0\0" + encoded_id
    offset = data.find(marker)
    if offset < 0:
        return None

    name, cursor = read_c_string(data, offset + len(marker), 64)
    if cursor + 4 > len(data):
        raise ValueError(f"cor ausente no item {item_id}")
    color = struct.unpack_from("<I", data, cursor)[0]
    model, cursor = read_c_string(data, cursor + 4, 260)
    if cursor + 13 > len(data):
        raise ValueError(f"campos fixos ausentes no item {item_id}")

    character_mask = data[cursor]
    required_level = data[cursor + 1]
    gold = struct.unpack_from("<I", data, cursor + 2)[0]
    cash = struct.unpack_from("<I", data, cursor + 6)[0]
    shop = data[cursor + 10]
    flags = data[cursor + 11]
    item_type = data[cursor + 12]
    return {
        "offset": offset,
        "name": name,
        "model": model.replace("\\", "/"),
        "color": color,
        "character_mask": character_mask,
        "required_level": required_level,
        "gold": gold,
        "cash": cash,
        "shop": shop,
        "flags": flags,
        "type": item_type,
    }


def normalize_alias(value: str) -> str:
    normalized = re.sub(r"[^a-z0-9]", "", value.lower())
    return normalized.replace("penzer", "panzer").replace("assult", "assault")


def parse_stage_classes(directory: Path) -> set[str]:
    classes = set()
    for path in directory.iterdir():
        if not re.fullmatch(r"stage_\d{3}\.txt", path.name, re.IGNORECASE):
            continue
        text = re.sub(r"//[^\r\n]*", "", path.read_text(encoding="latin1"))
        classes.update(match.group(1).strip().lower() for match in re.finditer(
            r"\bclass\s*=\s*\[([^\]]+)]", text, re.IGNORECASE))
    return classes


def parse_active_ids(raw: str | None) -> set[int]:
    if not raw:
        return set()
    return {int(value.strip()) for value in raw.split(",") if value.strip()}


def parse_language_data(data: bytes) -> dict[int, str]:
    result = {}
    for raw_line in data.decode("latin1").splitlines():
        parts = raw_line.split("\t", 1)
        if len(parts) != 2 or not parts[0].isdigit():
            continue
        result[int(parts[0])] = parts[1]
    return result


def creature_data(data: bytes, index: int) -> dict:
    start = index * CREATURE_RECORD_SIZE
    end = start + CREATURE_RECORD_SIZE
    if end > len(data):
        raise ValueError(f"creatures.dat truncado no registro {index}")
    record = data[start:end]
    result = {
        "record_offset": start,
        "record_sha256": hashlib.sha256(record).hexdigest(),
    }
    for name, serialized_offset, _, scalar_format, _ in NPC_LEVEL_FIELDS:
        result[name] = list(struct.unpack_from(
            f"<{NPC_LEVEL_COUNT}{scalar_format}", record, serialized_offset))
    return result


def creature_format(language_bytes: bytes | None) -> dict:
    language = parse_language_data(language_bytes) if language_bytes else {}
    return {
        "level_count": NPC_LEVEL_COUNT,
        "runtime_level_record_size": 160,
        "serialized_bytes_per_creature": CREATURE_RECORD_SIZE,
        "cumulative_cell_exp_offset": CUMULATIVE_EXP_OFFSET,
        "npc_kill_cp_reward_offset": NPC_KILL_CP_REWARD_OFFSET,
        "summon_cp_cost_offset": SUMMON_CP_COST_OFFSET,
        "unconsumed_field_1a4c_offset": UNCONSUMED_FIELD_1A4C_OFFSET,
        "level_fields": [
            {
                "name": name,
                "serialized_offset": serialized_offset,
                "runtime_offset": runtime_offset,
                "scalar_format": scalar_format,
                "language_id": language_id,
                "client_label": language.get(language_id) if language_id else None,
            }
            for name, serialized_offset, runtime_offset, scalar_format, language_id
            in NPC_LEVEL_FIELDS
        ],
        "secondary_records_per_creature": SECONDARY_RECORDS_PER_CREATURE,
        "secondary_record_size": SECONDARY_RECORD_SIZE,
    }


def build_catalog_data(creature_list_bytes: bytes, item_bytes: bytes,
                       stage_directory: Path | None = None,
                       raw_creatures: bytes | None = None,
                       active_item_ids: set[int] | None = None,
                       language_bytes: bytes | None = None) -> dict:
    entries = parse_creature_list_data(creature_list_bytes)
    stage_classes = parse_stage_classes(stage_directory) if stage_directory else set()
    active_ids = active_item_ids or set()
    if raw_creatures is not None and len(raw_creatures) < len(entries) * CREATURE_RECORD_SIZE:
        raise ValueError("creatures.dat menor que a área dos registros de creaturelist")

    aliases_by_name: dict[str, list[str]] = {}
    rows = []
    for index, ecl in enumerate(entries):
        item_id = ITEM_BASE + index
        item = parse_item_record(item_bytes, item_id)
        aliases = []
        if item:
            aliases = sorted(alias for alias in stage_classes
                             if normalize_alias(alias) == normalize_alias(item["name"]))
            aliases_by_name[normalize_alias(item["name"])] = aliases
        row = {
            "index": index,
            "item_id": item_id,
            "ecl": ecl,
            "active_in_sql": item_id in active_ids,
            "stage_aliases": aliases,
            "item": item,
        }
        if raw_creatures is not None:
            row["creatures_data"] = creature_data(raw_creatures, index)
        rows.append(row)

    mapped_aliases = {alias for row in rows for alias in row["stage_aliases"]}
    result = {
        "creature_count": len(entries),
        "items_sha256": hashlib.sha256(item_bytes).hexdigest(),
        "creatures": rows,
        "stage_classes": sorted(stage_classes),
        "unmapped_stage_classes": sorted(stage_classes - mapped_aliases),
    }
    if raw_creatures is not None:
        core_size = len(entries) * CREATURE_RECORD_SIZE
        expected_tail_size = (len(entries) * SECONDARY_RECORDS_PER_CREATURE *
                              SECONDARY_RECORD_SIZE)
        actual_tail_size = len(raw_creatures) - core_size
        result["creatures_data_sha256"] = hashlib.sha256(raw_creatures).hexdigest()
        result["creatures_data_format"] = creature_format(language_bytes)
        result["creatures_data_trailing_bytes"] = actual_tail_size
        result["creatures_data_trailing_expected_bytes"] = expected_tail_size
        result["creatures_data_layout_complete"] = actual_tail_size == expected_tail_size
        result["creatures_data_trailing_sha256"] = hashlib.sha256(
            raw_creatures[core_size:]).hexdigest()
    return result


def build_catalog(creature_list: Path, items_data: Path,
                  stage_directory: Path | None = None,
                  creatures_data: Path | None = None,
                  active_item_ids: set[int] | None = None) -> dict:
    raw_creatures = creatures_data.read_bytes() if creatures_data else None
    return build_catalog_data(
        creature_list.read_bytes(), items_data.read_bytes(), stage_directory,
        raw_creatures, active_item_ids)


def read_xfs_entries(path: Path) -> tuple[bytes, bytes, bytes, bytes]:
    try:
        from tools.xfs_read import decode_block, parse
    except ModuleNotFoundError:
        from xfs_read import decode_block, parse

    with contextlib.redirect_stdout(io.StringIO()):
        raw, entries = parse(str(path))

    def read_entry(suffix: str) -> bytes:
        matches = [entry for entry in entries if entry[0].lower().endswith(suffix)]
        if len(matches) != 1:
            raise ValueError(f"entrada {suffix!r} não é única em {path}")
        name, offset, compressed, uncompressed_size, compressed_size = matches[0]
        payload = raw[offset:offset + compressed_size]
        return decode_block(payload, compressed, uncompressed_size, name)

    return (read_entry("creaturelist.txt"), read_entry("items.dat"),
            read_entry("creatures.dat"), read_entry("language.txt"))


def summary(catalog: dict) -> str:
    lines = ["idx\titem\tactive\tname\tecl\tmodel\tstage_aliases"]
    for row in catalog["creatures"]:
        item = row["item"] or {}
        lines.append("\t".join((
            str(row["index"]), str(row["item_id"]),
            "yes" if row["active_in_sql"] else "no",
            item.get("name", "-"), row["ecl"], item.get("model", "-"),
            ",".join(row["stage_aliases"]) or "-")))
    lines.append("UNMAPPED\t" + ",".join(catalog["unmapped_stage_classes"]))
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Cruza creaturelist, items.dat, creatures.dat e classes de stage do Rakion v258")
    parser.add_argument("creature_list", type=Path, nargs="?")
    parser.add_argument("items_data", type=Path, nargs="?")
    parser.add_argument("--data-setup-xfs", type=Path)
    parser.add_argument("--stage-directory", type=Path)
    parser.add_argument("--creatures-data", type=Path)
    parser.add_argument("--active-item-ids")
    parser.add_argument("--summary", action="store_true")
    args = parser.parse_args()
    active_ids = parse_active_ids(args.active_item_ids)
    if args.data_setup_xfs:
        creature_list, items_data, creatures_data, language_data = read_xfs_entries(
            args.data_setup_xfs)
        catalog = build_catalog_data(
            creature_list, items_data, args.stage_directory, creatures_data, active_ids,
            language_data)
    else:
        if args.creature_list is None or args.items_data is None:
            parser.error("informe creature_list e items_data, ou use --data-setup-xfs")
        catalog = build_catalog(
            args.creature_list, args.items_data, args.stage_directory,
            args.creatures_data, active_ids)
    print(summary(catalog) if args.summary else json.dumps(catalog, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
