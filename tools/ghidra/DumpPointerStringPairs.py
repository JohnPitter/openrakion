# Decodifica tabelas [char* nome, u32 id] a partir de start/count.
# @category Rakion
from jarray import zeros

memory = currentProgram.getMemory()
listing = currentProgram.getListing()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
args = getScriptArgs()

if len(args) != 2:
    raise ValueError("uso: DumpPointerStringPairs.py <start> <count>")

start = space.getAddress(int(args[0], 0))
count = int(args[1], 0)
print("PROGRAM=%s START=%s COUNT=%d" % (currentProgram.getName(), start, count))
for index in range(count):
    entry = start.add(index * 8)
    pointer_value = memory.getInt(entry) & 0xffffffff
    identifier = memory.getInt(entry.add(4)) & 0xffffffff
    pointer = space.getAddress(pointer_value)
    data = listing.getDataAt(pointer)
    value = str(data.getValue()) if data is not None and data.hasStringValue() else "-"
    print("%s id=0x%08x ptr=%s name=%s" % (entry, identifier, pointer, value))

print("##### DONE #####")
