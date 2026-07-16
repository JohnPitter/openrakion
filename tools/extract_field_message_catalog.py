#!/usr/bin/env python3
"""Gera o catálogo canônico do dispatcher CNet de gameplay do cliente v258."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path


ENGINE_SHA256 = "83b20d6c32cd66b95c8f8e41ad6de13a58e8f5f948cd21cbd118d42ef8cf88f2"
RAKION_SHA256 = "435f50e3ff9f3f140d4c335336b4ba4a758df823c146210cc8da90460960ffff"

CASES = {
    0x307: ("Create general NPC", "u8 owner, u8 index, u16 entity, 6*f32 placement, init blob"),
    0x308: ("Create Master Golem", "u8 host, u8 team, u16 entity, 6*f32 placement, init blob"),
    0x309: ("Create map NPC", "u8 host, u8 index, u16 entity, 6*f32 placement, init blob"),
    0x30A: ("Player action/movement", "CNetMessage de ação serializada"),
    0x30B: ("Entity placement/state", "u16 state, u8 kind/group/index, 4*s16 placement"),
    0x30C: ("Entity event", "u8 source/class/indexA/indexB, u32 event, u32 length, payload"),
    0x30F: ("Remote player action", "u8 player slot"),
    0x310: ("Map NPC state/action", "u8 state, u8 kind, u8 map index"),
    0x312: ("Map item snapshot", "u8 count, count * (u8 index, u8 state)"),
}

OUTER_CASES = (
    "0x0201, 0x0203, 0x0304, 0x0305, 0x030E, 0x0311, 0x0313, 0x0314, "
    "0x0315, 0x0318, 0x0401, 0x0402, 0x0403, 0x0501 e 0x0502"
)


def parse_catalog(path: Path) -> list[int]:
    values: list[int] = []
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if not line.strip():
            continue
        logical_type, dispatcher = line.split("\t", 1)
        if dispatcher.lower() != "0x3610d7c0":
            raise ValueError(f"dispatcher inesperado: {dispatcher}")
        value = int(logical_type, 0)
        if value in values:
            raise ValueError(f"case duplicado: {logical_type}")
        values.append(value)
    if set(values) != set(CASES):
        raise ValueError(f"cases divergentes: {[hex(value) for value in values]}")
    return sorted(values)


def render(values: list[int], engine_sha: str, rakion_sha: str) -> str:
    lines = [
        "# Dispatcher CNet de gameplay — Rakion v258",
        "",
        f"Golden sources: `engine.dll` SHA-256 `{engine_sha}`; `rakion.bin` SHA-256",
        f"`{rakion_sha}`.",
        "",
        "O pump real é `rakion.bin:0x004124A0`: ele drena `CNet::RecvData` e entrega cada mensagem",
        "a `rakion.bin:0x00411760`. Esse dispatcher trata diretamente transporte, diagnóstico e",
        "algumas ações; quando o estado da partida é válido, o `default` encaminha gameplay para",
        "`CSessionState::HandleMessage @ engine.dll:0x3610D7C0`.",
        "",
        "Os cases tratados diretamente pelo executável são " + OUTER_CASES + ". Os nove cases",
        "de gameplay delegados e seus layouts estáticos são:",
        "",
        "| Tipo lógico | Semântica | Corpo consumido |",
        "|---:|---|---|",
    ]
    for value in values:
        name, layout = CASES[value]
        lines.append(f"| `0x{value:04X}` | {name} | `{layout}` |")
    lines.extend(
        [
            "",
            "No UDP reliable, o transporte acrescenta `0x8000` ao tipo lógico; por exemplo,",
            "`0x030C` aparece no fio como `0x830C`. ACK `0x4000`, sequência e slot de origem são",
            "metadados de transporte e não pertencem ao payload acima.",
            "",
            "`worldserv!FUN_0041B940` não produz mensagens CNet. Ela grava a fila de requests DB no formato",
            "`[u16 requestSequence][u16 commandType][data]`; `FUN_0041B3F0/FUN_0041AE50` a consomem.",
            "Na saída, o comando `0x0C [characterId][remainingExp]` executa `UPDATE CharacterInfo.exp`",
            "em `FUN_004138B0`; somente seu ACK interno não possui consumer. O retorno cliente-visível é",
            "WorldNet `0x58 [i32 remainingExp]`, sem relação com o evento CNet `0x030C`.",
            "",
            f"Total: **{len(values)} cases de gameplay delegados** e **15 cases diretos** no dispatcher externo.",
            "",
        ]
    )
    return "\n".join(lines)


def checked_sha(path: Path, expected: str, label: str) -> str:
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual != expected:
        raise ValueError(f"{label} divergente: {actual}")
    return actual


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog-tsv", type=Path, required=True)
    parser.add_argument("--engine", type=Path, required=True)
    parser.add_argument("--rakion", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(
        render(
            parse_catalog(args.catalog_tsv),
            checked_sha(args.engine, ENGINE_SHA256, "engine.dll"),
            checked_sha(args.rakion, RAKION_SHA256, "rakion.bin"),
        ),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
