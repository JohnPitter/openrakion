# -*- coding: utf-8 -*-
# Extrai tabelas compiladas [u32 value, char* name] pelos símbolos *_values.
# @category Rakion

memory = currentProgram.getMemory()
symbols = currentProgram.getSymbolTable()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
args = getScriptArgs()
raw_tables = []
needles = []
for value in args:
    if value.lower().startswith("0x"):
        address_text, separator, label = value.partition(":")
        raw_tables.append((label if separator else address_text,
                           space.getAddress(int(address_text, 0))))
    else:
        needles.append(value.lower())

if not needles and not raw_tables:
    raise ValueError("informe ao menos um trecho do símbolo *_values")


def read_string(address):
    if not memory.contains(address):
        return None
    chars = []
    for offset in range(128):
        cursor = address.add(offset)
        if not memory.contains(cursor):
            return None
        value = memory.getByte(cursor) & 0xff
        if value == 0:
            return "".join(chars) if chars else None
        if value < 0x20 or value > 0x7e:
            return None
        chars.append(chr(value))
    return None


matches = []
for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True)
    lowered = name.lower()
    if "_values" in lowered and any(needle in lowered for needle in needles):
        matches.append(symbol)

tables = [(symbol.getName(True), symbol.getAddress()) for symbol in matches] + raw_tables
print("PROGRAM=%s TABLES=%d" % (currentProgram.getName(), len(tables)))
for table_name, table_address in tables:
    print("\nTABLE %s @ %s" % (table_name, table_address))
    for index in range(256):
        entry = table_address.add(index * 8)
        if not memory.contains(entry.add(7)):
            break
        block = memory.getBlock(entry)
        if block is None or not block.isInitialized():
            break
        try:
            value = memory.getInt(entry) & 0xffffffff
            pointer = space.getAddress(memory.getInt(entry.add(4)) & 0xffffffff)
        except Exception:
            break
        name = read_string(pointer)
        if name is None:
            break
        print("%d\t0x%08X\t%s" % (index, value, name))

print("##### DONE #####")
