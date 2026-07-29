import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median


ROUND_START = "0x004B"
ROUND_END = "0x0050"
DEATH_REPORT = "0x004F"
COMBO_GAP_MS = 900


def read_events(path: Path):
    with path.open(encoding="utf-8-sig") as handle:
        for line in handle:
            if line.strip():
                yield json.loads(line)


def is_world_request(event: dict, type_value: str) -> bool:
    return (
        event.get("stream") == "world"
        and event.get("direction") == "C2S"
        and event.get("type") == type_value
    )


def round_window(path: Path) -> tuple[int, int]:
    start = None
    for event in read_events(path):
        relative_ms = int(event["relative_ms"])
        if start is None and is_world_request(event, ROUND_START):
            start = relative_ms
            continue
        if start is not None and is_world_request(event, ROUND_END):
            return start, relative_ms
    raise ValueError("não foi possível delimitar o round por 0x004B/0x0050")


def animation(event: dict) -> tuple[str, tuple[int, ...]] | None:
    if event.get("stream") != "local_peer_action":
        return None
    body = (event.get("decoded") or {}).get("body", {})
    kind = body.get("animation_kind")
    arguments = body.get("arguments")
    if not kind or not isinstance(arguments, list):
        return None
    return kind, tuple(int(value) for value in arguments)


def flush_combo(combo: list[int], counts: Counter) -> None:
    if combo:
        counts[tuple(combo)] += 1
        combo.clear()


def transition_stats(
    events: list[tuple[int, int]],
) -> dict[tuple[int, int], dict[str, int]]:
    samples = defaultdict(list)
    for previous, current in zip(events, events[1:]):
        elapsed = current[0] - previous[0]
        if elapsed <= COMBO_GAP_MS:
            samples[(previous[1], current[1])].append(elapsed)
    return {
        pair: {
            "count": len(values),
            "median_ms": int(median(values)),
            "min_ms": min(values),
            "max_ms": max(values),
        }
        for pair, values in samples.items()
    }


def analyze(path: Path) -> dict:
    start, end = round_window(path)
    attacks = defaultdict(Counter)
    normal = defaultdict(Counter)
    damage = defaultdict(Counter)
    attack_events = defaultdict(list)
    deaths = []

    for event in read_events(path):
        relative_ms = int(event["relative_ms"])
        if relative_ms < start or relative_ms > end:
            continue
        if is_world_request(event, DEATH_REPORT):
            deaths.append({
                "offset_ms": relative_ms - start,
                "seat": event.get("seat"),
                "hex": event.get("hex"),
            })
        decoded = animation(event)
        if decoded is None:
            continue
        pid = str(event.get("pid"))
        kind, arguments = decoded
        if kind == "Attack" and arguments:
            attacks[pid][arguments[0]] += 1
            attack_events[pid].append((relative_ms, arguments[0]))
        elif kind == "Normal" and arguments:
            normal[pid][arguments[0]] += 1
        elif kind == "Damage":
            damage[pid][arguments] += 1

    combos = {}
    transitions = {}
    for pid, events in attack_events.items():
        counts = Counter()
        current = []
        previous = None
        for relative_ms, attack_id in events:
            if previous is not None and relative_ms - previous > COMBO_GAP_MS:
                flush_combo(current, counts)
            current.append(attack_id)
            previous = relative_ms
        flush_combo(current, counts)
        combos[pid] = counts
        transitions[pid] = transition_stats(events)

    return {
        "start_ms": start,
        "end_ms": end,
        "duration_ms": end - start,
        "deaths": deaths,
        "attacks": dict(attacks),
        "normal_animations": dict(normal),
        "damage_animations": dict(damage),
        "attack_sequences": combos,
        "attack_transitions": transitions,
    }


def top_lines(values: Counter, formatter=str, limit: int = 15) -> str:
    if not values:
        return "- nenhum"
    return "\n".join(
        f"- `{formatter(key)}`: {count}"
        for key, count in values.most_common(limit)
    )


def sequence_name(values: tuple[int, ...]) -> str:
    return " -> ".join(str(value) for value in values)


def write_report(directory: Path, result: dict) -> None:
    serializable = dict(result)
    serializable["attacks"] = {
        pid: dict(values) for pid, values in result["attacks"].items()
    }
    serializable["normal_animations"] = {
        pid: dict(values)
        for pid, values in result["normal_animations"].items()
    }
    serializable["damage_animations"] = {
        pid: {"-".join(map(str, key)): count for key, count in values.items()}
        for pid, values in result["damage_animations"].items()
    }
    serializable["attack_sequences"] = {
        pid: {"-".join(map(str, key)): count for key, count in values.items()}
        for pid, values in result["attack_sequences"].items()
    }
    serializable["attack_transitions"] = {
        pid: {"-".join(map(str, key)): value for key, value in values.items()}
        for pid, values in result["attack_transitions"].items()
    }
    (directory / "round-analysis.json").write_text(
        json.dumps(serializable, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    sections = []
    for pid in sorted(result["attacks"]):
        sections.append(
            f"""### Cliente {pid}

Ataques:

{top_lines(result["attacks"][pid])}

Sequências com intervalo máximo de {COMBO_GAP_MS} ms:

{top_lines(result["attack_sequences"][pid], sequence_name)}

Reações de dano:

{top_lines(result["damage_animations"][pid], sequence_name)}

Intervalos entre fases:

{transition_lines(result["attack_transitions"][pid])}
"""
        )
    death_lines = "\n".join(
        f"- `+{item['offset_ms']} ms`: seat `{item['seat']}`, payload `{item['hex']}`"
        for item in result["deaths"]
    ) or "- nenhuma"
    report = f"""# Análise do round humano x humano

- Janela: `{result["start_ms"]}` a `{result["end_ms"]}` ms
- Duração: `{result["duration_ms"]}` ms
- Reportes de morte `0x004F`: `{len(result["deaths"])}`

## Mortes

{death_lines}

## Animações por cliente

{"".join(sections)}
"""
    (directory / "round-analysis.md").write_text(report, encoding="utf-8")


def transition_lines(values: dict[tuple[int, int], dict[str, int]]) -> str:
    if not values:
        return "- nenhum"
    ordered = sorted(
        values.items(),
        key=lambda item: (-item[1]["count"], item[0]),
    )
    return "\n".join(
        f"- `{sequence_name(pair)}`: n={stats['count']}, "
        f"mediana={stats['median_ms']} ms, "
        f"faixa={stats['min_ms']}..{stats['max_ms']} ms"
        for pair, stats in ordered[:20]
    )


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Extrai o round útil de uma timeline humano x humano"
    )
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    directory = args.directory.resolve()
    result = analyze(directory / "timeline.jsonl")
    write_report(directory, result)


if __name__ == "__main__":
    main()
