# -*- coding: utf-8 -*-
# Exibe strings ASCII nos enderecos informados.
# @category Rakion


memory = currentProgram.getMemory()


def read_ascii(address, limit=256):
    chars = []
    cursor = toAddr(address)
    for _ in range(limit):
        value = memory.getByte(cursor) & 0xff
        if value == 0:
            break
        chars.append(chr(value) if 0x20 <= value < 0x7f else "?")
        cursor = cursor.add(1)
    return "".join(chars)


print("PROGRAM=%s" % currentProgram.getName())
for raw in getScriptArgs():
    address = int(raw, 0)
    print("%08x %s" % (address, read_ascii(address)))
