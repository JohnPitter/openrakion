import argparse
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path

try:
    from tools.extract_entity_init_serializers import map_pe_image, parse_objdump_exports
except ModuleNotFoundError:
    from extract_entity_init_serializers import map_pe_image, parse_objdump_exports


GET_SIZE_RE = re.compile(r"^\?GetSizeOf@([^@]+)@@")


@dataclass(frozen=True)
class EventExport:
    name: str
    get_size_va: int
    constructor_va: int | None


@dataclass(frozen=True)
class EventRuntime:
    name: str
    get_size_va: int
    constructor_va: int | None
    total_size: int | None
    event_id: int | None
    status: str


def build_export_inventory(exports: dict[str, int], image_base: int) -> list[EventExport]:
    result = []
    for symbol, rva in exports.items():
        match = GET_SIZE_RE.match(symbol)
        if match is None or not match.group(1).startswith("E"):
            continue
        name = match.group(1)
        constructor = exports.get(f"??0{name}@@QAE@XZ")
        result.append(EventExport(
            name,
            image_base + rva,
            image_base + constructor if constructor is not None else None,
        ))
    return sorted(result, key=lambda item: item.name.lower())


def format_ghidra_input(events: list[EventExport]) -> str:
    lines = ["name\tget_size_va\tconstructor_va"]
    for event in events:
        constructor = f"0x{event.constructor_va:08X}" if event.constructor_va else "-"
        lines.append(f"{event.name}\t0x{event.get_size_va:08X}\t{constructor}")
    return "\n".join(lines) + "\n"


def parse_runtime_report(value: str) -> list[EventRuntime]:
    result = []
    for line_number, line in enumerate(value.splitlines(), 1):
        if not line or line.startswith("name\t"):
            continue
        columns = line.split("\t")
        if len(columns) != 6:
            raise ValueError(f"linha runtime {line_number} possui {len(columns)} colunas")
        name, get_size, constructor, total_size, event_id, status = columns
        result.append(EventRuntime(
            name,
            int(get_size, 0),
            None if constructor == "-" else int(constructor, 0),
            None if total_size == "-" else int(total_size, 0),
            None if event_id == "-" else int(event_id, 0),
            status,
        ))
    return result


def validate_runtime(events: list[EventRuntime]) -> None:
    names = set()
    for event in events:
        if event.name in names:
            raise ValueError(f"evento duplicado: {event.name}")
        names.add(event.name)
        if event.status != "ok" or event.total_size is None or event.event_id is None:
            continue
        if event.total_size < 8 or event.total_size > 0x10000:
            raise ValueError(f"tamanho inválido em {event.name}: {event.total_size}")


def validate_runtime_source(
        runtime: list[EventRuntime], exports: list[EventExport]) -> None:
    expected = {event.name: event for event in exports}
    if {event.name for event in runtime} != set(expected):
        raise ValueError("relatório runtime diverge do inventário de exports")
    for event in runtime:
        source = expected[event.name]
        if (event.get_size_va != source.get_size_va or
                event.constructor_va != source.constructor_va):
            raise ValueError(f"endereços runtime divergentes em {event.name}")


def format_markdown(events: list[EventRuntime], module_sha256: str) -> str:
    resolved = [event for event in events if event.status == "ok"]
    unresolved = [event for event in events if event.status != "ok"]
    lines = [
        "# Catálogo compilado de eventos de entidade — Rakion v258",
        "",
        "Este arquivo é gerado por `tools/extract_entity_event_catalog.py` a partir dos exports",
        "de `entitiesmp.dll` e do relatório runtime de `DumpClientEntityEventCatalog.py`.",
        "`total size` inclui o cabeçalho base de oito bytes; `payload size = total size - 8`.",
        "A presença no binário comprova o contrato da classe, não que todo evento seja emitido",
        "durante uma partida Rakion desta build.",
        "IDs pequenos podem se repetir entre classes: o dispatcher resolve o evento junto com a",
        "classe da entidade de destino, portanto o ID isolado não é uma chave global.",
        "",
        f"- SHA-256 de `entitiesmp.dll`: `{module_sha256.lower()}`",
        f"- classes `E*` com `GetSizeOf`: {len(events)}",
        f"- ID e tamanho resolvidos: {len(resolved)}",
        f"- não resolvidos: {len(unresolved)}",
        "",
        "## Eventos reliable de arma, hold e dano fechados",
        "",
        "A decompilação de construtores, cópias, produtores e consumidores do player fecha",
        "os corpos abaixo. `vec3f` são três `float32` little-endian. Nomes como `entityWord`",
        "permanecem neutros quando o binário prova o uso, mas não oferece um nome de domínio",
        "inequívoco para o campo.",
        "",
        "| ID | Classe | Layout do payload | Evidência de uso |",
        "|---:|---|---|---|",
        "| `0x01910006` | `ESetWeapon` | `i32 weaponSelector; i32 argument` | o primeiro word seleciona os dois caminhos de arma em `CPlayerAnimator::SetWeapon` |",
        "| `0x01910007` | `EShootWeapon` | `vec3f first; vec3f second; u8 shootType; u8 reserved[3]` | `shootType` usa a enum compilada `EShootWeaponType` com valores `0..2` |",
        "| `0x01910008` | `EShootShuriken` | `vec3f first; vec3f second; u8 projectileCount; u8 variant; u16 reserved` | o consumidor itera exatamente `projectileCount`; o produtor observado grava `9` |",
        "| `0x01910009` | `ERequestHoldAttack` | `u32 entityWord; u8 entityIndex; u8 entitySubIndex; u16 reserved; f32 maximumDistance; u32 argument` | `CheckHoldAttack` resolve a entidade e compara a distância com `maximumDistance` |",
        "| `0x0191000A` | `EHoldAttack` | `u32 entityWord; u8 entityIndex; u8 entitySubIndex; u16 reserved0; u32 argument; u8 actorIndex; u8 actorSubIndex; u16 reserved1` | `ExecuteHoldAttack` resolve a entidade e encaminha o hold ao alvo |",
        "| `0x0191000B` | `EPlayerDamage` | `u32 playerId; u8 damageType; u8 damageMotionType; u16 reserved; f32 firstDamageValue; f32 secondDamageValue; vec3f first; vec3f second` | `ReceiveDamage` copia tipos/vetores de `DamageInfo`, calcula os dois escalares e chama `ApplyReceiveDamage` |",
        "| `0x01910016` | `EPlayerDeath` | `vec3f deathVector` | o produtor copia os três componentes do primeiro vetor de `DamageInfo+0x58` |",
        "| `0x01910017` | `ERespawn` | vazio | o construtor contém somente a base de oito bytes |",
        "",
        "| Event ID | Classe | Total | Payload | GetSizeOf | Construtor | Estado |",
        "|---:|---|---:|---:|---:|---:|---|",
    ]
    for event in sorted(events, key=lambda item: (
            item.event_id is None, item.event_id or 0, item.name.lower())):
        event_id = f"`0x{event.event_id:08X}`" if event.event_id is not None else "—"
        total = str(event.total_size) if event.total_size is not None else "—"
        payload = str(event.total_size - 8) if event.total_size is not None else "—"
        constructor = (
            f"`0x{event.constructor_va:08X}`" if event.constructor_va is not None else "—")
        lines.append(
            f"| {event_id} | `{event.name}` | {total} | {payload} | "
            f"`0x{event.get_size_va:08X}` | {constructor} | `{event.status}` |")
    return "\n".join(lines) + "\n"


def sha256(path: Path) -> str:
    import hashlib
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Gera inventário/catálogo dos eventos exportados por entitiesmp.dll")
    parser.add_argument("--module", type=Path, required=True)
    parser.add_argument("--objdump", default="objdump")
    parser.add_argument("--ghidra-input", type=Path)
    parser.add_argument("--runtime-report", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    output = subprocess.run(
        [args.objdump, "-p", str(args.module)], check=True, capture_output=True,
        text=True, encoding="utf-8", errors="replace").stdout
    exports = parse_objdump_exports(output)
    image_base, _ = map_pe_image(args.module.read_bytes())
    events = build_export_inventory(exports, image_base)
    if args.ghidra_input:
        args.ghidra_input.write_text(format_ghidra_input(events), encoding="utf-8")
    if args.runtime_report:
        runtime = parse_runtime_report(args.runtime_report.read_text(encoding="utf-8"))
        validate_runtime_source(runtime, events)
        validate_runtime(runtime)
        markdown = format_markdown(runtime, sha256(args.module))
        if args.output:
            args.output.write_text(markdown, encoding="utf-8")
        else:
            print(markdown, end="")
    elif not args.ghidra_input:
        print(format_ghidra_input(events), end="")


if __name__ == "__main__":
    main()
