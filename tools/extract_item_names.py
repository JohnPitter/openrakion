import struct, re, io

buf = open(r'C:\temp\items_dat_raw.bin', 'rb').read()
n = len(buf)
LBL = re.compile(rb'^[A-Za-z0-9()+. _\-]{2,28}$')
PATH = re.compile(rb'(ModelsSV|TexturesSV|Scripts)', re.I)
FF = b'\xff\xff\xff\xff'

def cstr(off, maxlen=30):
    j = off
    while j < n and buf[j] != 0 and j - off < maxlen: j += 1
    return buf[off:j], j

def pr(c): return 32 <= c < 127

names = {}
i = 0
while i + 28 < n:
    a = struct.unpack_from('<I', buf, i)[0]
    if 1000 <= a <= 14999 and a not in names:
        # varre a janela [i+4, i+24] por inicios de string (run de printaveis)
        for p in range(i + 4, i + 26):
            if pr(buf[p]) and not pr(buf[p - 1]):
                raw, j = cstr(p)
                if j < n and buf[j] == 0 and LBL.match(raw) and not raw.lower().endswith((b'.tex', b'.smc', b'.lua')):
                    tail = buf[j:j + 280]
                    if tail[1:5] == FF or PATH.search(tail):
                        names[a] = raw.decode('latin-1', 'replace')
                        break
    i += 1

print("itens com nome: %d / ~1000" % len(names))
pl = [1005,1013,1015,1018,1101,1201,1301,1401,1501,1008,1230,1405,9012,9017,12000,12002,13001,13003,8001]
miss = [x for x in pl if x not in names]
print("cobertura do player: %d/19" % (19 - len(miss)))
for iid in pl: print("  %-6d %s" % (iid, names.get(iid, '<<falta>>')))
print("faltam:", miss)
with io.open(r'C:\temp\item_names.tsv', 'w', encoding='utf-8') as f:
    for k in sorted(names): f.write("%d\t%s\n" % (k, names[k]))
print("salvo %d itens" % len(names))
