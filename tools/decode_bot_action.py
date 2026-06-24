"""Decodifica os pacotes UDP de gameplay de C:\\temp\\botcap.log (saída do mitm_botcap.py) e isola o
move/ação 0x30a do bot. DESCOBRE o framing REAL do datagrama (qual marker/envelope o cliente usa) e
decodifica o corpo 0x30a (pos/heading/aim) p/ golden-confirmar contra BotMovement (server .NET).

O datagrama esperado (RE engine.dll FUN_36100ef0): [u16 0x030a|0x830a][u32 seq][u8 srcSlot][body 19B].
Corpo (GetActionFromMessage): [u16 dt][u8 (act<<5)|slot][u8 0][s16 x][s16 y][s16 z][s16 head][u8 flag]
[s16 ax][s16 ay][s16 az]; x/y/z e ax/ay/az = short*0.01 (= coord). Mas o ENVELOPE pode diferir (o
relay .NET viu markers 0x0104) — por isso aqui a gente DESCOBRE, não assume.

Uso: python tools/decode_bot_action.py [caminho_log]
"""
import sys, re, struct
from collections import Counter

LOG = sys.argv[1] if len(sys.argv) > 1 else r"C:\temp\botcap.log"
SCALE = 0.01
udp_re = re.compile(r"(C->W|W->C)\s+UDP:(\d+)\s+(\d+)B\s+([0-9a-fA-F]+)")

pkts = []
with open(LOG, "r", errors="ignore") as f:
    for line in f:
        m = udp_re.search(line)
        if m:
            d, port, nb, data = m.groups()
            pkts.append((d, int(port), int(nb), bytes.fromhex(data)))

print("=" * 72)
print("UDP: %d pacotes" % len(pkts))
print("=" * 72)

# 1) famílias por (direção, marker 2B, tamanho) — revela o envelope real
fam = Counter()
for d, port, nb, b in pkts:
    fam[(d, b[:2].hex() if len(b) >= 2 else "", nb)] += 1
print("\n### famílias (direção, marker[:2], tamanho) — as mais comuns ###")
for (d, mk, nb), c in sorted(fam.items(), key=lambda x: -x[1])[:20]:
    print("  %-5s marker=%-6s %3dB  x%d" % (d, mk, nb, c))

def find_30a(b):
    """Acha o offset do CNetMessage 0x30a (bytes 0a 03) dentro do datagrama; -1 se não houver."""
    for off in range(0, len(b) - 1):
        if b[off] == 0x0a and b[off + 1] == 0x03:
            return off
    return -1

def decode_body(b, o):
    """Decodifica o corpo 0x30a começando em o (logo após o [u16 0x030a])."""
    if o + 19 > len(b):
        return None
    dt = struct.unpack_from("<H", b, o)[0]
    actslot = b[o + 2]
    x = struct.unpack_from("<h", b, o + 4)[0] * SCALE
    y = struct.unpack_from("<h", b, o + 6)[0] * SCALE
    z = struct.unpack_from("<h", b, o + 8)[0] * SCALE
    head = struct.unpack_from("<h", b, o + 10)[0]
    flag = b[o + 12]
    ax = struct.unpack_from("<h", b, o + 13)[0] * SCALE
    ay = struct.unpack_from("<h", b, o + 15)[0] * SCALE
    az = struct.unpack_from("<h", b, o + 17)[0] * SCALE
    return dict(dt=dt, act=actslot >> 5, slot=actslot & 0x1f, x=x, y=y, z=z,
               head=head, flag=flag, ax=ax, ay=ay, az=az)

# 2) decodifica os candidatos a 0x30a
print("\n### candidatos a MOVE/AÇÃO 0x30a (corpo decodificado) ###")
n = 0
for d, port, nb, b in pkts:
    off = find_30a(b)
    if off < 0:
        continue
    body = decode_body(b, off + 2)  # +2 = pula o [u16 0x030a]
    if not body:
        continue
    env = b[:off].hex()  # o ENVELOPE antes do 0x30a (seq/src/marker) — o que falta cravar
    print("  %-5s %3dB env=%s slot=%2d act=%d pos=(%.2f,%.2f,%.2f) head=%d flag=%d aim=(%.2f,%.2f,%.2f)"
          % (d, nb, env or "(nenhum)", body["slot"], body["act"], body["x"], body["y"], body["z"],
             body["head"], body["flag"], body["ax"], body["ay"], body["az"]))
    n += 1
    if n >= 30:
        print("  ... (limitado a 30; total acima)")
        break

if n == 0:
    print("  NENHUM 0x30a achado. Verifique: entrou numa sala e ANDOU? O ação pode usar outro envelope —")
    print("  veja as 'famílias' acima (markers/tamanhos) e me mande o log que eu identifico o frame.")
else:
    print("\n>> 'env=' é o ENVELOPE a cravar (seq+srcSlot+marker). Andar em X/Y deve mover pos.(x/y)")
    print(">> monotonicamente; girar muda head. Com isso eu confirmo BotMovement e ligo UdpFramingKnown.")
