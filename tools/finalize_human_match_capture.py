import argparse
import csv
import hashlib
import json
import re
import struct
from collections import Counter
from datetime import datetime, timedelta
from pathlib import Path

try:
    from tools.decode_gameplay_p2p import decode
except ModuleNotFoundError:
    from decode_gameplay_p2p import decode


BUTTONS = {
    0x00000001: "Attack",
    0x00000004: "Space",
    0x00000020: "W",
    0x00100000: "S",
    0x08000000: "A",
    0x10000000: "D",
}


def load_session(directory: Path) -> dict:
    path = directory / "session.json"
    if not path.exists():
        raise FileNotFoundError(f"metadados ausentes: {path}")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def capture_pid(path: Path) -> int | None:
    match = re.search(r"_(\d+)\.csv$", path.name)
    return int(match.group(1)) if match else None


def client_relative_ms(tick: int, start_tick: int) -> int:
    return (tick - (start_tick & 0xFFFFFFFF)) & 0xFFFFFFFF


def event_time(start: datetime, relative_ms: int) -> str:
    return (start + timedelta(milliseconds=relative_ms)).isoformat()


def decode_action_payload(type_value: int, payload: bytes) -> dict | None:
    if type_value == 0x030A and len(payload) == 19:
        source = payload[2] & 0x1F
    elif type_value in {0x030F, 0x0311} and payload:
        source = payload[0]
    else:
        return None
    packet = struct.pack("<HIB", type_value, 0, source) + payload
    return decode(packet)


def decode_action_buffer(payload: bytes) -> dict:
    if len(payload) < 72:
        return {"raw_size": len(payload)}
    buttons = int.from_bytes(payload[0x10:0x14], "little")
    controls = [name for flag, name in BUTTONS.items() if buttons & flag]
    result = {
        "buttons": f"0x{buttons:08X}",
        "controls": controls,
        "translation_x": struct.unpack_from("<f", payload, 0x40)[0],
        "translation_y": struct.unpack_from("<f", payload, 0x44)[0],
    }
    if len(payload) >= 76:
        result["translation_z"] = struct.unpack_from("<f", payload, 0x48)[0]
    return result


def summarize(decoded: dict | None) -> str:
    if not decoded:
        return "raw"
    logical_type = decoded.get("logical_type")
    body = decoded.get("body", {})
    if logical_type == "0x030A":
        position = body.get("position", [])
        return (
            f"move seat={decoded.get('source_slot')} pos={position} "
            f"state={body.get('player_action_state')} "
            f"action={body.get('action_name', body.get('action_code'))}"
        )
    if logical_type == "0x030F":
        return (
            f"sync seat={decoded.get('source_slot')} "
            f"life={body.get('life_state')} animator={body.get('animator_value')} "
            f"control={body.get('control_mode')}/{body.get('control_detail')}"
        )
    if logical_type == "0x0311":
        return (
            f"animation seat={decoded.get('source_slot')} "
            f"kind={body.get('animation_kind')} args={body.get('arguments')}"
        )
    if "event_name" in body:
        return (
            f"event seat={body.get('sender_slot')} {body['event_name']} "
            f"primary={body.get('primary_entity_slot')} "
            f"secondary={body.get('secondary_entity_slot')}"
        )
    return f"datagram {logical_type or decoded.get('raw_type', 'unknown')}"


def base_event(
    relative_ms: int,
    start: datetime,
    origin: str,
    stream: str,
    pid: int | None) -> dict:
    return {
        "relative_ms": relative_ms,
        "utc": event_time(start, relative_ms),
        "origin": origin,
        "stream": stream,
        "pid": pid,
    }


def load_action_capture(
    path: Path,
    start: datetime,
    start_tick: int) -> list[dict]:
    events = []
    with path.open(newline="", encoding="ascii") as handle:
        for row in csv.reader(handle):
            if len(row) != 4:
                continue
            tick, raw_type, declared_length, payload_hex = row
            payload = bytes.fromhex(payload_hex)
            type_value = int(raw_type, 16)
            decoded = decode_action_payload(type_value, payload)
            event = base_event(
                client_relative_ms(int(tick), start_tick),
                start,
                "client",
                "local_peer_action",
                capture_pid(path))
            event.update({
                "direction": "out",
                "channel": "P2P",
                "type": f"0x{type_value:04X}",
                "length": int(declared_length),
                "summary": summarize(decoded),
                "decoded": decoded,
                "hex": payload_hex.upper(),
            })
            events.append(event)
    return events


def load_action_buffers(
    path: Path,
    start: datetime,
    start_tick: int,
    stream: str) -> list[dict]:
    events = []
    with path.open(newline="", encoding="ascii") as handle:
        for row in csv.reader(handle):
            minimum = 3 if stream == "local_action_buffer" else 2
            if len(row) != minimum:
                continue
            tick = int(row[0])
            payload_hex = row[-1]
            decoded = decode_action_buffer(bytes.fromhex(payload_hex))
            event = base_event(
                client_relative_ms(tick, start_tick),
                start,
                "client",
                stream,
                capture_pid(path))
            event.update({
                "direction": "local" if stream == "local_action_buffer" else "in",
                "channel": "ENGINE",
                "type": "action_buffer",
                "length": len(payload_hex) // 2,
                "summary": f"controls={decoded.get('controls', [])}",
                "decoded": decoded,
                "hex": payload_hex.upper(),
            })
            events.append(event)
    return events


def try_decode_packet(payload: bytes) -> dict | None:
    try:
        return decode(payload)
    except (ValueError, IndexError, struct.error):
        return None


def load_socket_capture(
    path: Path,
    start: datetime,
    start_tick: int,
    direction: str) -> list[dict]:
    events = []
    with path.open(newline="", encoding="ascii") as handle:
        for row in csv.reader(handle):
            if len(row) != 4:
                continue
            tick, port, declared_length, payload_hex = row
            payload = bytes.fromhex(payload_hex)
            decoded = try_decode_packet(payload)
            event = base_event(
                client_relative_ms(int(tick), start_tick),
                start,
                "client",
                "socket",
                capture_pid(path))
            event.update({
                "direction": direction,
                "channel": f"UDP:{port}",
                "type": decoded.get("logical_type") if decoded else "raw",
                "length": int(declared_length),
                "summary": summarize(decoded),
                "decoded": decoded,
                "hex": payload_hex.upper(),
            })
            events.append(event)
    return events


def load_server_capture(
    path: Path,
    start: datetime,
    start_tick: int) -> list[dict]:
    events = []
    if not path.exists():
        return events
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip():
            continue
        value = json.loads(line)
        relative = max(0, int(value["tick"]) - start_tick)
        event = base_event(relative, start, "server", "world", None)
        event.update({
            "direction": value["direction"],
            "channel": value["channel"],
            "type": value["type"],
            "field": value["field"],
            "seat": value["seat"],
            "status": value["status"],
            "length": value["length"],
            "summary": value["detail"],
            "decoded": None,
            "hex": value["hex"],
        })
        events.append(event)
    return events


def load_events(directory: Path, session: dict) -> list[dict]:
    start = datetime.fromisoformat(session["startUtc"])
    start_tick = int(session["startTick"])
    events = load_server_capture(
        directory / "server_match.jsonl", start, start_tick)
    for path in directory.glob("openrakion_action_capture_*.csv"):
        events.extend(load_action_capture(path, start, start_tick))
    for path in directory.glob("openrakion_player_action_*.csv"):
        events.extend(load_action_buffers(
            path, start, start_tick, "local_action_buffer"))
    for path in directory.glob("openrakion_remote_action_*.csv"):
        events.extend(load_action_buffers(
            path, start, start_tick, "remote_action_buffer"))
    for path in directory.glob("openrakion_provider_send_*.csv"):
        events.extend(load_socket_capture(path, start, start_tick, "out"))
    for path in directory.glob("openrakion_socket_receive_*.csv"):
        events.extend(load_socket_capture(path, start, start_tick, "in"))
    return sorted(
        events,
        key=lambda event: (
            event["relative_ms"],
            event["origin"],
            event.get("pid") or 0,
            event["stream"]))


def write_timeline(directory: Path, events: list[dict]) -> None:
    jsonl = directory / "timeline.jsonl"
    with jsonl.open("w", encoding="utf-8", newline="\n") as handle:
        for event in events:
            handle.write(json.dumps(event, ensure_ascii=False) + "\n")

    fields = [
        "relative_ms", "utc", "origin", "pid", "stream", "direction",
        "channel", "field", "seat", "status", "type", "length",
        "summary", "decoded", "hex",
    ]
    with (directory / "timeline.csv").open(
        "w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        for event in events:
            row = dict(event)
            row["decoded"] = json.dumps(
                row.get("decoded"), ensure_ascii=False, separators=(",", ":"))
            writer.writerow(row)


def capture_counts(events: list[dict]) -> dict[str, Counter]:
    result = {
        "streams": Counter(),
        "types": Counter(),
        "animations": Counter(),
        "events": Counter(),
        "controls": Counter(),
    }
    for event in events:
        result["streams"][event["stream"]] += 1
        result["types"][event.get("type", "unknown")] += 1
        decoded = event.get("decoded") or {}
        body = decoded.get("body", {})
        if body.get("animation_kind"):
            key = f"{body['animation_kind']}:{body.get('arguments')}"
            result["animations"][key] += 1
        if body.get("event_name"):
            result["events"][body["event_name"]] += 1
        for control in decoded.get("controls", []):
            result["controls"][control] += 1
    return result


def format_counter(counter: Counter) -> str:
    if not counter:
        return "- nenhum"
    return "\n".join(
        f"- `{key}`: {count}"
        for key, count in counter.most_common())


def write_summary(directory: Path, events: list[dict], session: dict) -> None:
    counts = capture_counts(events)
    pids = sorted({
        event["pid"] for event in events if event.get("pid") is not None
    })
    duration = events[-1]["relative_ms"] if events else 0
    text = f"""# Captura humano x humano

- Início UTC: `{session["startUtc"]}`
- Duração observada: `{duration} ms`
- Clientes: `{", ".join(map(str, pids)) or "nenhum"}`
- Eventos correlacionados: `{len(events)}`

## Streams

{format_counter(counts["streams"])}

## Tipos de datagrama

{format_counter(counts["types"])}

## Animações

{format_counter(counts["animations"])}

## Eventos de entidade

{format_counter(counts["events"])}

## Controles locais

{format_counter(counts["controls"])}
"""
    (directory / "summary.md").write_text(text, encoding="utf-8")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def write_manifest(directory: Path) -> None:
    files = []
    for path in sorted(directory.iterdir()):
        if not path.is_file() or path.name == "manifest.json":
            continue
        files.append({
            "name": path.name,
            "size": path.stat().st_size,
            "sha256": sha256(path),
        })
    (directory / "manifest.json").write_text(
        json.dumps({"files": files}, indent=2, ensure_ascii=False),
        encoding="utf-8")


def finalize(directory: Path) -> None:
    session = load_session(directory)
    events = load_events(directory, session)
    write_timeline(directory, events)
    write_summary(directory, events, session)
    write_manifest(directory)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Correlaciona a captura completa de uma partida humano x humano")
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    finalize(args.directory.resolve())


if __name__ == "__main__":
    main()
