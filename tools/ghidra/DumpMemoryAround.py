# Exibe bytes e dwords ao redor dos enderecos passados.
# @category Rakion
from jarray import zeros

memory = currentProgram.getMemory()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()

print("PROGRAM=%s" % currentProgram.getName())
for raw in getScriptArgs():
    center = space.getAddress(int(raw, 0))
    start = center.subtract(0x40)
    data = zeros(0x80, "b")
    count = memory.getBytes(start, data)
    print("\nCENTER=%s START=%s BYTES=%d" % (center, start, count))
    for row in range(0, count, 0x10):
        chunk = [(value + 0x100) & 0xff for value in data[row:row + 0x10]]
        print("  %s  %s" % (
            start.add(row),
            " ".join("%02x" % value for value in chunk),
        ))
    print("  DWORDS")
    for row in range(0, count - 3, 4):
        values = [(data[row + index] + 0x100) & 0xff for index in range(4)]
        value = values[0] | values[1] << 8 | values[2] << 16 | values[3] << 24
        print("    %s = %08x" % (start.add(row), value))

print("##### DONE #####")
