#@category Rakion

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (0x00406B10, 0x0041BCA0)
OUTPUT = r"C:\temp\world_room_info.txt"

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for address in ADDRESSES:
        function = getFunctionAt(toAddr(address))
        if function is None:
            output.write("MISSING 0x%08X\n" % address)
            continue
        result = decompiler.decompileFunction(function, 90, monitor)
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(), function.getEntryPoint()))
        output.write(result.getDecompiledFunction().getC())
        output.write("\n")

print("World /roominfo: %d funcoes -> %s" % (len(ADDRESSES), OUTPUT))
