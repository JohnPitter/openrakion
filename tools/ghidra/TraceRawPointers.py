# Localiza ponteiros little-endian brutos para enderecos e seus containers.
# @category Rakion
from jarray import array

memory = currentProgram.getMemory()
listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
monitor = getMonitor()


def pattern32(value):
    values = [
        value & 0xff,
        (value >> 8) & 0xff,
        (value >> 16) & 0xff,
        (value >> 24) & 0xff,
    ]
    return array([item if item < 0x80 else item - 0x100 for item in values], "b")


print("PROGRAM=%s" % currentProgram.getName())
for raw in getScriptArgs():
    target_value = int(raw, 0) & 0xffffffff
    target = space.getAddress(target_value)
    pattern = pattern32(target_value)
    hits = []
    for block in memory.getBlocks():
        if not block.isInitialized():
            continue
        cursor = block.getStart()
        while cursor is not None and cursor.compareTo(block.getEnd()) <= 0:
            hit = memory.findBytes(cursor, block.getEnd(), pattern, None, True, monitor)
            if hit is None:
                break
            hits.append(hit)
            if hit.equals(block.getEnd()):
                break
            cursor = hit.add(1)

    print("\nTARGET=%s RAW_POINTERS=%d" % (target, len(hits)))
    for hit in hits:
        function = functions.getFunctionContaining(hit)
        data = listing.getDataContaining(hit)
        print("  PTR=%s FUNCTION=%s DATA=%s" % (
            hit,
            function.getName() if function else "-",
            data.toString() if data else "-",
        ))

print("##### DONE #####")
