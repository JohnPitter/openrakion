# Dumps a contiguous 32-bit vtable and decompiles each referenced function.
# @category Rakion.RE

from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.address import Address


def parse_address(value):
    return currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(value)


def read_pointer(address):
    raw = getBytes(address, 4)
    value = 0
    for index in range(4):
        value |= (raw[index] & 0xff) << (index * 8)
    return parse_address("0x%08x" % value)


arguments = getScriptArgs()
if len(arguments) < 2:
    printerr("Uso: DumpVtable.py <address> <entry-count> [summary]")
    exit()

vtable = parse_address(arguments[0])
entry_count = int(arguments[1], 0)
summary_only = len(arguments) > 2 and arguments[2].lower() == "summary"
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)

println("PROGRAM=%s" % currentProgram.getName())
println("VTABLE=%s ENTRIES=%d" % (vtable, entry_count))

for index in range(entry_count):
    slot = vtable.add(index * 4)
    target = read_pointer(slot)
    function = getFunctionAt(target)
    if function is None:
        function = getFunctionContaining(target)

    if function is None:
        println("\n[%02x] slot=%s target=%s function=<none>" % (index * 4, slot, target))
        continue

    println("\n[%02x] slot=%s target=%s function=%s" % (
        index * 4,
        slot,
        target,
        function.getName(),
    ))
    if summary_only:
        continue

    result = decompiler.decompileFunction(function, 30, monitor)
    if result.decompileCompleted():
        println(result.getDecompiledFunction().getC())
    else:
        println("<decompile failed: %s>" % result.getErrorMessage())

println("\n##### DONE #####")
