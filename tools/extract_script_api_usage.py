import argparse
import json
import re
from pathlib import Path
from typing import Iterable

try:
    from tools.xfs_read import decode_block, parse
except ModuleNotFoundError:
    from xfs_read import decode_block, parse


DEFAULT_APIS = ("GetCP", "AddCP", "ReduceCP", "SetCP", "GetMaxCP")


def find_api_usage(
    entries: Iterable[tuple[str, bytes]], api_names: Iterable[str]
) -> list[dict]:
    names = tuple(dict.fromkeys(api_names))
    patterns = {
        name: re.compile(r"(?<![A-Za-z0-9_])" + re.escape(name) + r"(?![A-Za-z0-9_])")
        for name in names
    }
    matches = []
    for entry_name, payload in entries:
        text = payload.decode("latin-1", "replace")
        lines = []
        used = set()
        for number, line in enumerate(text.splitlines(), 1):
            line_apis = [name for name, pattern in patterns.items() if pattern.search(line)]
            if not line_apis:
                continue
            used.update(line_apis)
            lines.append({"number": number, "apis": line_apis, "text": line.strip()})
        if used:
            matches.append({
                "entry": entry_name,
                "apis": [name for name in names if name in used],
                "lines": lines,
            })
    return matches


def read_xfs_entries(path: Path) -> list[tuple[str, bytes]]:
    archive, records = parse(str(path), verbose=False)
    entries = []
    for name, offset, compressed, size, compressed_size in records:
        raw = archive[offset:offset + compressed_size]
        entries.append((name, decode_block(raw, compressed, size, name)))
    return entries


def build_report(path: Path, api_names: Iterable[str]) -> dict:
    entries = read_xfs_entries(path)
    names = tuple(dict.fromkeys(api_names))
    return {
        "source": str(path),
        "entry_count": len(entries),
        "apis": list(names),
        "matches": find_api_usage(entries, names),
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Localiza usos de APIs em todos os scripts de um arquivo XFS."
    )
    parser.add_argument("scripts_xfs", type=Path)
    parser.add_argument("apis", nargs="*", default=DEFAULT_APIS)
    args = parser.parse_args()
    print(json.dumps(build_report(args.scripts_xfs, args.apis), indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
