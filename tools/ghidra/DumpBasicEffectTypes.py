# -*- coding: utf-8 -*-
# Extrai a enum BasicEffectType diretamente da tabela compilada de entitiesmp.dll.
# @category Rakion


VALUES = 0x3537FC20
COUNT = 0x4C
OUTPUT = r"C:\temp\basic_effect_types.txt"

memory = currentProgram.getMemory()


def read_c_string(address):
    result = []
    cursor = address
    for _ in range(256):
        value = memory.getByte(cursor) & 0xff
        if value == 0:
            break
        result.append(chr(value))
        cursor = cursor.add(1)
    return "".join(result)


with open(OUTPUT, "w") as output:
    output.write("BasicEffectType_values=0x%08X count=%d\n" % (VALUES, COUNT))
    for index in range(COUNT):
        entry = toAddr(VALUES + index * 8)
        value = memory.getInt(entry)
        name_address = toAddr(memory.getInt(entry.add(4)) & 0xffffffff)
        output.write("0x%02X\t%s\n" % (value & 0xffffffff, read_c_string(name_address)))

print("BasicEffectType extraido em " + OUTPUT)
