#!/usr/bin/env python3
"""Gera o catálogo do dispatcher IScavengerWorldNet S→C do cliente v258."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path


ENGINE_SHA256 = "83b20d6c32cd66b95c8f8e41ad6de13a58e8f5f948cd21cbd118d42ef8cf88f2"
RAKION_SHA256 = "435f50e3ff9f3f140d4c335336b4ba4a758df823c146210cc8da90460960ffff"

SIMPLE_RESPONSE_CONTRACTS = (
    (0x5C, "`cstr text`", "copia a string; callback final vazio", "sem produtor literal na build"),
    (0x63, "`cstr text`", "copia e encaminha a string", "`FUN_0041F290`"),
    (0x67, "corpo não lido", "callback sem argumentos e vazio", "sem produtor literal na build"),
    (0x68, "corpo não lido", "callback sem argumentos e vazio", "sem produtor literal na build"),
    (0x69, "ponteiro bruto", "callback recebe o endereço e retorna", "sem produtor literal na build"),
    (0x6A, "ponteiro bruto", "callback recebe o endereço e retorna", "`FUN_0041C330/0041D650`"),
)


def parse_dispatch(path: Path) -> list[tuple[int, str, str]]:
    rows: list[tuple[int, str, str]] = []
    seen: set[int] = set()
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if not line.strip():
            continue
        opcode_text, handler, destination = line.split("\t", 2)
        opcode = int(opcode_text, 0)
        if opcode in seen or handler == "-" or not destination:
            raise ValueError(f"case inválido ou duplicado: {line}")
        seen.add(opcode)
        rows.append((opcode, handler, destination))
    if len(rows) != 88:
        raise ValueError(f"dispatcher esperado com 88 cases, obtido {len(rows)}")
    return sorted(rows)


def response_family(opcode: int) -> str:
    if opcode <= 0x10:
        return "sessão/login"
    if opcode <= 0x1C:
        return "personagem"
    if opcode <= 0x2A:
        return "canal"
    if opcode <= 0x35 or opcode in {0x6F, 0x70, 0x71, 0x73, 0x74}:
        return "inventário/progressão"
    if opcode <= 0x43 or opcode == 0x72:
        return "lista/sala"
    if opcode <= 0x63:
        return "field/partida"
    return "eventos/presentes"


def parse_callbacks(path: Path) -> dict[int, tuple[str, str]]:
    rows: dict[int, tuple[str, str]] = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if not line.strip():
            continue
        opcode_text, destination, implementation = line.split("\t", 2)
        opcode = int(opcode_text, 0)
        if opcode in rows or not destination or not implementation:
            raise ValueError(f"callback inválido ou duplicado: {line}")
        rows[opcode] = (destination, implementation)
    if len(rows) != 88:
        raise ValueError(f"callbacks esperados para 88 cases, obtidos {len(rows)}")
    return rows


def render(
    rows: list[tuple[int, str, str]],
    callbacks: dict[int, tuple[str, str]],
    engine_sha: str,
    rakion_sha: str,
) -> str:
    for opcode, _, destination in rows:
        if callbacks.get(opcode, (None, None))[0] != destination:
            raise ValueError(f"destino divergente para 0x{opcode:02X}")
    lines = [
        "# Catálogo do dispatcher IScavengerWorldNet S→C — Rakion v258",
        "",
        f"Golden sources: `engine.dll` SHA-256 `{engine_sha}`; `rakion.bin` SHA-256",
        f"`{rakion_sha}`; dispatcher `0x36197320`; vtable final `0x004DDC08`.",
        "",
        "Este catálogo enumera todos os cases aceitos pela fila de respostas `IScavengerWorldNet`.",
        "`ProcessWorldRecvBuffer @ 0x36197A40` é seu único caller na `engine.dll`; o executável chama",
        "esse export uma vez por iteração principal em `rakion.bin:0x004126BD`. Os cases FIELD abaixo",
        "são respostas de controle que também passam por WorldNet. `worldserv!FUN_0041B940` não é",
        "outro stream cliente: ela alimenta a fila de requisições do worker DB do World.",
        "A família indica contexto funcional esperado e não substitui a causalidade/canal por estado.",
        "",
        "| Opcode | Handler engine | Destino | Implementação rakion.bin | Família funcional |",
        "|---:|---:|---:|---:|---|",
    ]
    lines.extend(
        f"| `0x{opcode:02X}` | `{handler}` | `{destination}` | "
        f"`{callbacks[opcode][1]}` | {response_family(opcode)} |"
        for opcode, handler, destination in rows
    )
    lines.extend(
        [
            "",
            f"Total: **{len(rows)} cases**, sem handler ausente ou opcode duplicado.",
            "",
            "Observações estáticas fechadas:",
            "",
            "- `0x61` não chama a UI: remonta e devolve `[u16 0x61][i32 value]` ao World;",
            "- `0x04`, `0x05`, `0x29`, `0x2A`, `0x59`, `0x5A`, `0x5C` e `0x67..0x6A` apontam para funções vazias no `rakion.bin`;",
            "- em especial, `0x6A` não gera UI visual nesta build.",
            "",
            "Contratos de progressão fechados pelo consumidor e pelo produtor original:",
            "",
            "| Opcode | Payload lógico S→C | Consumidor | Efeito |",
            "|---:|---|---:|---|",
            "| `0x51` | `[u8 newLevel][u16 levelPoints]` | `0x36194100` | atualiza nível e pontos locais |",
            "| `0x52` | `[u8 seat][u8 playerLevel][u8 cellLevel0][u8 cellLevel1][u8 cellLevel2]` | `0x36194130` | aplica level-up ao player remoto e atualiza os três slots de cell do jogador local |",
            "",
            "O `u16` intermediário usado pelo builder original de `0x52` representa os dois primeiros",
            "níveis de cell em bytes little-endian; o consumidor encaminha os cinco bytes separadamente.",
            "`ProgressionResponseBodies` é a golden source de emissão no World .NET.",
            "",
            "Respostas simples ou dormentes auditadas:",
            "",
            "| Opcode | Consumo estrutural no engine | Callback Rakion | Produtor World |",
            "|---:|---|---|---|",
        ]
    )
    lines.extend(
        f"| `0x{opcode:02X}` | {layout} | {callback} | {producer} |"
        for opcode, layout, callback, producer in SIMPLE_RESPONSE_CONTRACTS
    )
    lines.extend(
        [
            "",
            "`corpo não lido` não equivale a payload obrigatoriamente vazio: o handler ignora qualquer",
            "byte posterior. `ponteiro bruto` também não define um `u32`; é o endereço do início do corpo.",
            "Somente o produtor pode fechar a gramática desses casos. Para `0x6A`, o produtor de presentes",
            "fecha `[count:u8][itemId:u32 * count][accountName:cstr]`, embora a UI final seja vazia.",
            "A busca de `0x5C/0x67..0x69` percorreu até quatro chamadas até os senders World. Em `0x5C`,",
            "todas as ocorrências eram offsets. Os únicos literais `0x67/0x69` e um dos `0x68` eram razões",
            "de disconnect em `FUN_00423CC0`; o outro `0x68` era stride de `IMUL` no sender. Essas APIs",
            "existem no cliente, mas não possuem produtor estático no `worldserv.exe` v258 analisado.",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dispatch-tsv", type=Path, required=True)
    parser.add_argument("--callbacks-tsv", type=Path, required=True)
    parser.add_argument("--engine", type=Path, required=True)
    parser.add_argument("--rakion", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    engine_sha = hashlib.sha256(args.engine.read_bytes()).hexdigest()
    if engine_sha != ENGINE_SHA256:
        raise ValueError(f"engine.dll divergente: {engine_sha}")
    rakion_sha = hashlib.sha256(args.rakion.read_bytes()).hexdigest()
    if rakion_sha != RAKION_SHA256:
        raise ValueError(f"rakion.bin divergente: {rakion_sha}")
    args.output.write_text(
        render(
            parse_dispatch(args.dispatch_tsv),
            parse_callbacks(args.callbacks_tsv),
            engine_sha,
            rakion_sha,
        ),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
