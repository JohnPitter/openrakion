import pymysql, io

# nomes reais (do items.dat) ja extraidos
names = {}
for line in open(r'C:\temp\item_names.tsv', encoding='utf-8'):
    a, b = line.rstrip('\n').split('\t', 1); names[int(a)] = b
real = len(names)

# iteminfo: type + level p/ o fallback
c = pymysql.connect(host="127.0.0.1", user="root", password="123456", database="rakion")
cur = c.cursor(); cur.execute("SELECT id, type, level FROM iteminfo"); rows = cur.fetchall(); c.close()

def kind(t):
    if 0 <= t <= 5: return "Equip"
    if t in (6, 7): return "Equip+"
    if t == 8: return "Transform"
    if t == 10: return "Arma"
    if t == 13: return "Poção"
    if t == 12: return "Item"
    return "Tipo %d" % t

added = 0
for iid, t, lvl in rows:
    if iid not in names:
        names[iid] = "%s Lv%d" % (kind(t), lvl) if lvl and lvl > 1 else kind(t)
        added += 1

with io.open(r'C:\temp\item_names.tsv', 'w', encoding='utf-8') as f:
    for k in sorted(names): f.write("%d\t%s\n" % (k, names[k]))

# cobertura sobre o iteminfo
ids = {r[0] for r in rows}
named = sum(1 for i in ids if i in names)
print("nomes reais (client): %d" % real)
print("fallback gerado (categoria+nivel): %d" % added)
print("iteminfo com rotulo agora: %d/%d (100%%)" % (named, len(ids)))
print("total no tsv: %d" % len(names))
# amostra dos fallbacks dos itens do player
for iid in (1015, 1016, 1024):
    print("  fallback %d -> %s" % (iid, names.get(iid)))
