"""Neutraliza o teste de velocidade de conexao P2P do cliente Rakion, que BLOQUEAVA a entrada na sala
quando o ping com o outro jogador passa de 300ms (ex.: 2 clientes no mesmo IP / P2P pelo Docker NAT).
RE estatica (agente) em rakion-work/ghidra-proj/conn_speed_check.out.txt.

4 sites (VA / file offset):
  0x447475 / 0x046875  cmp eax,0x12c   (RTT sala)   -> cmp eax,0x7fffffff
  0x447505 / 0x046905  cmp eax,0x12c   (RTT sala)   -> cmp eax,0x7fffffff
  0x40d288 / 0x00c688  cmp [eax],0x12c (RTT stage)  -> cmp [eax],0x7fffffff
  0x446ce0 / 0x0460e0  SetSpeedVerdictIfActive (funil de abort, __stdcall 1 arg) -> ret 4 (sem popup/abort)

Os 3 cmp fazem o RTT ser sempre tratado como rapido (pega o ramo OK). O ret 4 cobre tambem os aborts
de recusa/retry de UDP P2P (cinto-e-suspensorio). Aplica nos rakion.bin/exe LIVE (em cima do botbtn).
Idempotente (re-rodavel). Uso: py tools/patch_connfix.py
"""
import os

BIN_DIR = r"C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin"
TARGETS = ["rakion.exe", "rakion.bin"]

# (file_offset, bytes_orig, bytes_novos) — mesmo tamanho
PATCHES = [
    (0x046875, bytes.fromhex("3d2c010000"),   bytes.fromhex("3dffffff7f")),
    (0x046905, bytes.fromhex("3d2c010000"),   bytes.fromhex("3dffffff7f")),
    (0x00c688, bytes.fromhex("81382c010000"), bytes.fromhex("8138ffffff7f")),
    (0x0460e0, bytes.fromhex("8b81f8090000"), bytes.fromhex("c20400909090")),
]

for name in TARGETS:
    path = os.path.join(BIN_DIR, name)
    if not os.path.exists(path):
        print("[skip] %s nao existe" % name); continue
    data = bytearray(open(path, "rb").read())
    skip = False
    for off, orig, new in PATCHES:
        cur = bytes(data[off:off+len(orig)])
        if cur == new:
            continue  # ja aplicado
        if cur != orig:
            print("[!] %s @%#x: bytes inesperados %s (esperava %s) — PULANDO arquivo" % (name, off, cur.hex(), orig.hex()))
            skip = True; break
        data[off:off+len(new)] = new
    if not skip:
        open(path, "wb").write(data)
        print("OK %-11s -> teste de velocidade P2P neutralizado (4 sites)" % name)
