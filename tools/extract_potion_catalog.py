import argparse
import json
import re
import struct
from pathlib import Path

from xfs_read import decode_block, parse


POTION_MIN = 12000
POTION_MAX = 12999
SCRIPT_PATTERN = re.compile(rb"Scripts\\item\\(\d+)\.lua", re.IGNORECASE)


def xfs_entry(path: Path, suffix: str) -> bytes:
    archive, entries = parse(str(path), verbose=False)
    for name, offset, compressed, size, compressed_size in entries:
        if name.lower().endswith(suffix.lower()):
            return decode_block(
                archive[offset:offset + compressed_size], compressed, size, name)
    raise ValueError(f"entrada {suffix} ausente em {path}")


def potion_item_records(data: bytes) -> list[dict]:
    records = []
    for offset in range(len(data) - 10):
        item_id = struct.unpack_from("<I", data, offset)[0]
        if not POTION_MIN <= item_id <= POTION_MAX or data[offset + 4:offset + 6] != b"\0\0":
            continue
        family_id = struct.unpack_from("<I", data, offset + 6)[0]
        if not POTION_MIN <= family_id <= POTION_MAX:
            continue
        name_end = data.find(b"\0", offset + 10, offset + 80)
        if name_end < 0:
            continue
        raw_name = data[offset + 10:name_end]
        if not raw_name or any(value < 0x20 or value > 0x7e for value in raw_name):
            continue
        window = data[offset:min(len(data), offset + 400)]
        script_match = SCRIPT_PATTERN.search(window)
        if script_match is None:
            continue
        record = {
            "item_id": item_id,
            "family_id": family_id,
            "name": raw_name.decode("ascii"),
            "script_id": int(script_match.group(1)),
        }
        script_end = window.find(b"\0", script_match.end())
        description_end = window.find(b"\0", script_end + 1) if script_end >= 0 else -1
        if description_end > script_end + 1:
            description = window[script_end + 1:description_end]
            if all(value in (9, 10, 13) or 0x20 <= value <= 0x7e for value in description):
                record["description"] = description.decode("ascii")
        records.append(record)
    return records


def potion_scripts(path: Path) -> dict[int, str]:
    archive, entries = parse(str(path), verbose=False)
    scripts = {}
    for name, offset, compressed, size, compressed_size in entries:
        match = re.fullmatch(r"scripts\\item\\(12\d+)\.lua", name, re.IGNORECASE)
        if match is None:
            continue
        raw = decode_block(
            archive[offset:offset + compressed_size], compressed, size, name)
        scripts[int(match.group(1))] = raw.decode("cp949", "replace")
    return scripts


def script_semantics(script_id: int, text: str) -> dict:
    result = {"script_id": script_id}
    resource = re.search(r"Add(HP|AP|CP)\([^\n]*?\*\s*([0-9.]+)\)", text)
    if resource:
        result.update(effect="restore", resource=resource.group(1), ratio=float(resource.group(2)))
    hp_cost = re.search(r"ReduceHP\([^\n]*?\*\s*([0-9.]+)\)", text)
    if hp_cost:
        result.update(effect="steam", hp_cost_ratio=float(hp_cost.group(1)))
    sender = re.search(r"Use(HPPotion|APPotion|CPPotion|SteamPotion|HorroPotion1|HorroPotion2|ScouterPotion)\(", text)
    if sender:
        result["sender"] = "Use" + sender.group(1)
    chaos = re.search(r"UseChaosPotion\((\d+)\)", text)
    if chaos:
        result.update(effect="chaos", sender="UseChaosPotion", argument=int(chaos.group(1)))
    if "IsHoldAttack" in text:
        result["guard"] = "IsHoldAttack == 0"
    elif "IsChargeChaosPoint" in text:
        result["guard"] = "IsChargeChaosPoint"
    elif "return true" in text and "GetHP" not in text and "GetAP" not in text and "GetCP" not in text:
        result["guard"] = "always"
    return result


def build_catalog(data_setup: Path, scripts_xfs: Path) -> dict:
    items = potion_item_records(xfs_entry(data_setup, r"datasetup\items.dat"))
    scripts = potion_scripts(scripts_xfs)
    return {
        "item_count": len(items),
        "items": items,
        "scripts": [script_semantics(script_id, scripts[script_id]) for script_id in sorted(scripts)],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Extrai famílias e fórmulas de potion do cliente v258")
    parser.add_argument("--data-setup", type=Path, required=True)
    parser.add_argument("--scripts", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    output = json.dumps(build_catalog(args.data_setup, args.scripts), ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(output + "\n", encoding="utf-8")
    else:
        print(output)


if __name__ == "__main__":
    main()
