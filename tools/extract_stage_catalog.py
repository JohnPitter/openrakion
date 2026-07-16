import argparse
import hashlib
import json
import re
from collections import Counter
from pathlib import Path, PureWindowsPath

try:
    from tools.xfs_read import decode_block, parse as parse_xfs
except ModuleNotFoundError:
    from xfs_read import decode_block, parse as parse_xfs


STAGE_FILE = re.compile(r"stage_(\d{3})\.txt$", re.IGNORECASE)


def blocks(text: str):
    start = None
    depth = 0
    for index, char in enumerate(text):
        if char == "{":
            if depth == 0:
                start = index + 1
            depth += 1
        elif char == "}" and depth:
            depth -= 1
            if depth == 0 and start is not None:
                yield text[start:index]
                start = None


def value(text: str, key: str) -> str | None:
    match = re.search(rf"\b{re.escape(key)}\s*=\s*\[([^\]]*)\]", text, re.IGNORECASE)
    return match.group(1).strip() if match else None


def integer(text: str, key: str) -> int | None:
    raw = value(text, key)
    if raw is None:
        return None
    match = re.search(r"-?\d+", raw)
    return int(match.group()) if match else None


def number(text: str, key: str) -> float | None:
    raw = value(text, key)
    if raw is None:
        return None
    match = re.search(r"-?\d+(?:\.\d+)?", raw)
    return float(match.group()) if match else None


def names(text: str, key: str) -> list[str]:
    raw = value(text, key) or ""
    return [item.strip().lower() for item in raw.split(",") if item.strip()]


def rank_rows(stage_block: str) -> list[dict]:
    rows = []
    for match in re.finditer(r"\brankvar\s*=\s*\[([^\]]*)\]", stage_block, re.IGNORECASE):
        body = match.group(1)
        fields = {}
        for part in body.split("|"):
            pair = part.split("=", 1)
            if len(pair) == 2:
                fields[pair[0].strip().lower()] = pair[1].strip()
        if not {"class", "value", "gold", "exp"}.issubset(fields):
            continue
        rows.append({
            "rank": fields["class"].upper(),
            "value": int(fields["value"]),
            "gold": int(fields["gold"]),
            "exp": int(fields["exp"]),
            "multiplier": float(fields.get("multi", "1")),
        })
    return rows


def npc_rows(text: str) -> list[dict]:
    rows = []
    for block in blocks(text):
        if not re.search(r"\bNpcSpawn\s*:", block, re.IGNORECASE):
            continue
        npc_class = value(block, "class")
        level = integer(block, "level")
        names = value(block, "npcname") or ""
        if npc_class is None or level is None:
            continue
        flags = (value(block, "varset") or "").lower()
        rows.append({
            "name": value(block, "name") or "",
            "class": npc_class.strip().lower(),
            "level": level,
            "count": len([name for name in names.split(",") if name.strip()]),
            "friendly": "friendly" in flags,
            "target": "marked as target" in flags,
        })
    return rows


def flow_nodes(text: str) -> list[dict]:
    nodes = []
    for block in blocks(text):
        match = re.search(
            r"\b(Switch|Trigger|NpcSpawn|PopupMessage|WarpSpawn|BoxItem)\s*:\s*"
            r"name\s*=\s*\[([^\]]+)\]",
                          block, re.IGNORECASE)
        if match is None:
            continue
        kind = match.group(1).lower()
        nodes.append({
            "kind": kind,
            "name": match.group(2).strip().lower(),
            "delay": number(block, "delaytime") or 0,
            "condition": (value(block, "condition") or "").strip().lower(),
            "execution": (value(block, "execution") or "").strip().lower(),
            "targets": names(block, "target"),
            "link_switches": names(block, "linkswitch"),
            "link_triggers": names(block, "linktrigger"),
        })
    return nodes


def flow_audit(nodes: list[dict]) -> dict:
    by_name = {}
    duplicates = []
    for node in nodes:
        key = (node["kind"], node["name"])
        if key in by_name:
            duplicates.append(f'{node["kind"]}:{node["name"]}')
        else:
            by_name[key] = node

    broken = []
    adjacency = {(node["kind"], node["name"]): [] for node in nodes}
    for node in nodes:
        source = (node["kind"], node["name"])
        references = [(name, ("switch",)) for name in node["link_switches"]]
        references.extend((name, ("trigger", "popupmessage"))
                          for name in node["link_triggers"])
        if node["kind"] == "trigger" and node["execution"] == "spawn npc":
            references.extend((name, ("npcspawn",)) for name in node["targets"])
        if node["kind"] == "trigger" and node["execution"] == "spawn item":
            references.extend((name, ("boxitem",)) for name in node["targets"])
        for name, expected_kinds in references:
            target_key = next(((kind, name) for kind in expected_kinds
                               if (kind, name) in by_name), None)
            if target_key is None:
                broken.append({
                    "source": node["name"], "target": name,
                    "expectedKind": "/".join(expected_kinds),
                    "actualKind": None,
                })
                continue
            adjacency[source].append(target_key)

    roots = sorted((node["kind"], node["name"]) for node in nodes
                   if node["kind"] == "switch" and node["condition"] == "start")
    reachable = set(roots)
    queue = list(roots)
    while queue:
        current = queue.pop(0)
        for target in adjacency.get(current, []):
            if target not in reachable:
                reachable.add(target)
                queue.append(target)
    unreachable = sorted(set(by_name) - reachable)
    wins = sorted((node["kind"], node["name"]) for node in nodes
                  if node["kind"] == "trigger" and node["execution"] == "win")
    labels = lambda values: [f"{kind}:{name}" for kind, name in values]
    return {
        "roots": labels(roots),
        "nodeCount": len(nodes),
        "reachableNodeCount": len(reachable),
        "unreachableNodes": labels(unreachable),
        "duplicateNames": sorted(set(duplicates)),
        "brokenReferences": broken,
        "winTriggers": labels(wins),
        "reachableWinTriggers": labels(name for name in wins if name in reachable),
    }


def parse_stage(path: Path, raw: bytes | None = None) -> dict:
    raw = path.read_bytes() if raw is None else raw
    text = re.sub(r"//[^\r\n]*", "", raw.decode("latin1"))
    stage_match = STAGE_FILE.search(path.name)
    if stage_match is None:
        raise ValueError(f"nome de stage inválido: {path.name}")
    stage_block = next((block for block in blocks(text)
                        if re.search(r"\bStage\s*:", block, re.IGNORECASE)), None)
    if stage_block is None:
        raise ValueError(f"bloco Stage ausente: {path.name}")
    npcs = npc_rows(text)
    flow = flow_nodes(text)
    return {
        "id": int(stage_match.group(1)),
        "file": path.name,
        "sha256": hashlib.sha256(raw).hexdigest(),
        "map_id": integer(stage_block, "mapid"),
        "name": value(stage_block, "name"),
        "map": value(stage_block, "map"),
        "time_limit": integer(stage_block, "time_limit"),
        "goal": (value(stage_block, "goal") or "").lower(),
        "goal_argument": value(stage_block, "goalvar"),
        "goal_value": integer(stage_block, "goalvar"),
        "min_players": integer(stage_block, "player_min_number"),
        "max_players": integer(stage_block, "player_max_number"),
        "min_level": integer(stage_block, "player_low_level"),
        "max_level": integer(stage_block, "player_high_level"),
        "ranks": rank_rows(stage_block),
        "npc_spawns": npcs,
        "flow_nodes": flow,
        "flow_audit": flow_audit(flow),
    }


def normalized_goal(raw: str) -> str:
    compact = re.sub(r"\s+", " ", raw.strip().lower())
    return "time attack" if compact == "timeattack" else compact


def thresholds_are_consistent(stage: dict) -> bool:
    thresholds = [row["value"] for row in stage["ranks"]]
    if len(thresholds) != 5:
        return False
    goal = normalized_goal(stage["goal"])
    if goal == "time attack":
        return thresholds == sorted(thresholds) and all(
            value <= stage["time_limit"] for value in thresholds)
    if goal in {"butchery", "guard", "survival"}:
        return thresholds == sorted(thresholds, reverse=True)
    return False


def runtime_catalog(catalog: list[dict]) -> list[dict]:
    rank_values = {"D": 1, "C": 2, "B": 3, "A": 4, "S": 5}
    return [{
        "id": stage["id"],
        "sourceFile": stage["file"],
        "sourceSha256": stage["sha256"],
        "mapId": stage["map_id"],
        "timeLimitSeconds": stage["time_limit"],
        "goal": normalized_goal(stage["goal"]),
        "goalArgument": stage["goal_argument"],
        "minPlayers": stage["min_players"],
        "maxPlayers": stage["max_players"],
        "minLevel": stage["min_level"],
        "maxLevel": stage["max_level"],
        "rankThresholdsConsistent": thresholds_are_consistent(stage),
        "ranks": [{
            "rank": rank_values[row["rank"]],
            "threshold": row["value"],
            "exp": row["exp"],
            "gold": row["gold"],
            "multiplier": row["multiplier"],
        } for row in stage["ranks"]],
        "spawnDefinitionCount": len(stage["npc_spawns"]),
        "npcCount": sum(row["count"] for row in stage["npc_spawns"]),
        "flowNodeCount": stage["flow_audit"]["nodeCount"],
        "reachableFlowNodeCount": stage["flow_audit"]["reachableNodeCount"],
        "flowReferencesConsistent": not stage["flow_audit"]["brokenReferences"],
        "flowNamesUnique": not stage["flow_audit"]["duplicateNames"],
    } for stage in catalog]


def flow_catalog(catalog: list[dict]) -> list[dict]:
    return [{
        "id": stage["id"],
        "sourceFile": stage["file"],
        "sourceSha256": stage["sha256"],
        "audit": stage["flow_audit"],
        "nodes": stage["flow_nodes"],
    } for stage in catalog]


def load_catalog(directory: Path) -> list[dict]:
    if directory.is_file():
        raw_xfs, entries = parse_xfs(str(directory), verbose=False)
        sources = []
        for name, offset, compressed, uncompressed_size, compressed_size in entries:
            path = Path(PureWindowsPath(name).name)
            if STAGE_FILE.search(path.name) is None:
                continue
            raw = decode_block(
                raw_xfs[offset:offset + compressed_size], compressed,
                uncompressed_size, name)
            sources.append((path, raw))
    else:
        sources = [(path, None) for path in directory.iterdir()
                   if STAGE_FILE.search(path.name)]
    sources.sort(key=lambda source: int(STAGE_FILE.search(source[0].name).group(1)))
    catalog = [parse_stage(path, raw) for path, raw in sources]
    ids = [stage["id"] for stage in catalog]
    if len(ids) != len(set(ids)):
        raise ValueError("IDs de stage duplicados")
    return catalog


def summary(catalog: list[dict]) -> str:
    classes = Counter()
    total_npcs = 0
    lines = ["id\ttime\tgoal\tgoal_value\tranks\tspawn_defs\tnpcs\tclasses"]
    for stage in catalog:
        stage_classes = Counter()
        for spawn in stage["npc_spawns"]:
            stage_classes[spawn["class"]] += spawn["count"]
            classes[spawn["class"]] += spawn["count"]
            total_npcs += spawn["count"]
        ranks = ",".join(f'{row["rank"]}:{row["value"]}/{row["exp"]}/{row["gold"]}'
                         for row in stage["ranks"])
        class_text = ",".join(f"{name}:{count}" for name, count in sorted(stage_classes.items()))
        lines.append("\t".join(map(str, (
            stage["id"], stage["time_limit"], stage["goal"], stage["goal_value"], ranks,
            len(stage["npc_spawns"]), sum(stage_classes.values()), class_text))))
    lines.append(f"TOTAL\tstages={len(catalog)}\tspawn_defs="
                 f"{sum(len(stage['npc_spawns']) for stage in catalog)}\tnpcs={total_npcs}\t"
                 + ",".join(f"{name}:{count}" for name, count in sorted(classes.items())))
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description="Extrai o catálogo dos stage_*.txt do Rakion v258")
    parser.add_argument("directory", type=Path,
                        help="diretório LevelData ou DataSetup.xfs")
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--max-stage", type=int)
    parser.add_argument("--runtime-output", type=Path)
    parser.add_argument("--flow-output", type=Path)
    args = parser.parse_args()
    catalog = load_catalog(args.directory)
    if args.max_stage is not None:
        catalog = [stage for stage in catalog if stage["id"] <= args.max_stage]
    if args.runtime_output is not None:
        args.runtime_output.parent.mkdir(parents=True, exist_ok=True)
        args.runtime_output.write_text(
            json.dumps(runtime_catalog(catalog), ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
    if args.flow_output is not None:
        args.flow_output.parent.mkdir(parents=True, exist_ok=True)
        args.flow_output.write_text(
            json.dumps(flow_catalog(catalog), ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
    if args.runtime_output is None and args.flow_output is None:
        print(summary(catalog) if args.summary else json.dumps(catalog, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
