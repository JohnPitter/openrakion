#!/usr/bin/env python3
"""Diff das entidades dumpadas pela entitydiff.dll — acha o campo do gate do HIT×N.

Compara o slot do HUMANO-peer (recebe HIT×N) contra o slot do BOT (não recebe), no MESMO cliente.
Um offset é CANDIDATO A GATE quando, ao longo dos snapshots:
  - o valor do humano é ESTÁVEL (não é posição/anim que oscila),
  - o valor do bot é ESTÁVEL,
  - humano != bot (especialmente humano != 0 e bot == 0 = flag/ponteiro que o bot não tem).
Offsets que variam DENTRO do mesmo slot entre snapshots são ruído (posição, HP corrente, nonce) e saem.

Uso:  python diff_entities.py <dir> <human_slot> <bot_slot>
Ex.:  python diff_entities.py C:\\temp\\entdiff 10 11
"""
import sys, os, glob, struct

def load_slot(d, slot):
    """Todos os snapshots de um slot, em ordem. Lista de bytes."""
    files = sorted(glob.glob(os.path.join(d, f"slot{slot:02d}_snap*.bin")))
    return [open(f, "rb").read() for f in files]

def stable_value(snaps, off, width):
    """Se os bytes [off:off+width] são iguais em TODOS os snapshots, devolve o valor; senão None (varia=ruído)."""
    vals = set()
    for b in snaps:
        if off + width > len(b):
            return None
        vals.add(b[off:off+width])
        if len(vals) > 1:
            return None
    return next(iter(vals)) if vals else None

def as_u32(bs):
    return struct.unpack("<I", bs)[0] if bs and len(bs) == 4 else None

def main():
    if len(sys.argv) != 4:
        print(__doc__); sys.exit(1)
    d, hs, bs = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    hum = load_slot(d, hs)
    bot = load_slot(d, bs)
    if not hum or not bot:
        print(f"ERRO: faltam dumps (humano slot{hs}: {len(hum)} snaps, bot slot{bs}: {len(bot)} snaps)")
        sys.exit(1)
    n = min(len(hum[0]), len(bot[0]))
    print(f"# humano=slot{hs} ({len(hum)} snaps)  bot=slot{bs} ({len(bot)} snaps)  tamanho={n:#x}")
    print(f"# {'offset':>8}  {'humano(u32)':>12}  {'bot(u32)':>12}  nota")
    print("# " + "-"*56)

    hits = 0
    for off in range(0, n - 3, 4):   # varredura em dwords (alinhado)
        hv = stable_value(hum, off, 4)   # estável no humano?
        bv = stable_value(bot, off, 4)   # estável no bot?
        if hv is None or bv is None:     # varia em algum dos dois = ruído
            continue
        if hv == bv:                     # igual nos dois = não é o gate
            continue
        hu, bu = as_u32(hv), as_u32(bv)
        # prioriza os suspeitos: humano tem, bot não (flag zerada / ponteiro ausente) e vice-versa.
        nota = ""
        if hu and not bu: nota = "<<< humano TEM, bot NÃO (flag/ponteiro ausente no bot?)"
        elif bu and not hu: nota = "bot tem, humano não"
        elif hu is not None and bu is not None and abs(hu - bu) <= 3: nota = "diff pequeno (team/estado?)"
        print(f"  {off:#08x}  {hu:12}  {bu:12}  {nota}")
        hits += 1

    print(f"# {hits} offsets estáveis e divergentes. Foca os '<<<' — o campo que o bot não tem é o gate provável.")

if __name__ == "__main__":
    main()
