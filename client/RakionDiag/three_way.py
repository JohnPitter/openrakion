#!/usr/bin/env python3
"""Diff de TRÊS VIAS — isola o gate do HIT×N cortando o ruído de humano-vs-bot.

Ideia: dois players FUNCIONAM (dão HIT×N) — o local (slot 0) e o humano-peer remoto — e o BOT não.
O gate são os offsets onde os DOIS que funcionam CONCORDAM mas o bot DIFERE:
    valor(ok1) == valor(ok2) != valor(bot)
Isso remove tudo que é diferença legítima humano↔bot (aparência/classe/modelo/posição) e sobra o campo
de estado-de-combate (team/alive/HP/template/flag) que o bot não tem igual aos players reais.

Uso:  python three_way.py <dir> <ok1_slot> <ok2_slot> <bot_slot>
Ex.:  python three_way.py C:\\temp\\entdiff\\pid1336 0 10 11
      (0 = player local/host, 10 = humano-peer remoto, 11 = bot)
"""
import sys, os, glob, struct

def load(d, slot):
    fs = sorted(glob.glob(os.path.join(d, f"slot{slot:02d}_snap*.bin")))
    return [open(f, "rb").read() for f in fs]

def stable(snaps, off):
    """valor u32 em [off] se IGUAL em todos os snapshots; senão None (varia = ruído)."""
    v = None
    for b in snaps:
        if off + 4 > len(b): return None
        cur = struct.unpack("<I", b[off:off+4])[0]
        if v is None: v = cur
        elif v != cur: return None
    return v

def main():
    if len(sys.argv) != 5:
        print(__doc__); sys.exit(1)
    d, s1, s2, sb = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
    ok1, ok2, bot = load(d, s1), load(d, s2), load(d, sb)
    if not (ok1 and ok2 and bot):
        print(f"ERRO: faltam dumps (ok1={len(ok1)} ok2={len(ok2)} bot={len(bot)} snaps)"); sys.exit(1)
    n = min(len(ok1[0]), len(ok2[0]), len(bot[0]))
    print(f"# ok1=slot{s1} ok2=slot{s2} bot=slot{sb}  ({len(bot)} snaps, {n:#x} bytes)")
    print(f"# regra: ok1==ok2 (os dois que funcionam concordam) != bot  =>  candidato a GATE")
    print(f"# {'offset':>8}  {'ok(comum)':>12}  {'bot':>12}  nota")
    print("# " + "-"*58)

    cands = []
    for off in range(0, n - 3, 4):
        a1, a2, ab = stable(ok1, off), stable(ok2, off), stable(bot, off)
        if a1 is None or a2 is None or ab is None: continue  # algum varia = ruído
        if a1 != a2: continue        # os dois que funcionam TÊM de concordar
        if a1 == ab: continue        # bot igual = não é o gate
        cands.append((off, a1, ab))

    # rank: prioriza bot==0 (falta o campo), depois valores pequenos (flag/enum/HP/team)
    def score(c):
        off, ok, bot = c
        s = 0
        if bot == 0 and ok != 0: s -= 100       # o bot ZERA um campo que os players têm
        if ok <= 1024: s -= 20                  # valor pequeno = flag/enum/team/HP provável
        if ok in (1,2,3,100,400,0x41,0x64): s -= 10
        return s
    cands.sort(key=score)

    for off, ok, bot in cands:
        nota = ""
        if bot == 0 and ok != 0: nota = "<<< os players TÊM, o bot ZERA"
        elif ok == 0 and bot != 0: nota = "bot tem, players não"
        elif ok <= 1024 and bot <= 1024: nota = "valores pequenos (team/alive/HP/estado?)"
        print(f"  {off:#08x}  {ok:12}  {bot:12}  {nota}")
    print(f"# {len(cands)} candidatos. Os '<<<' no topo (bot zera o que os players têm) são o gate mais provável.")

if __name__ == "__main__":
    main()
