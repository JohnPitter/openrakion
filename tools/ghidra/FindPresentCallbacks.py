# Localiza callbacks que enviam os subtipos 0x6A..0x6D ao cliente.
# Saida: C:\temp\present_callbacks.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

SEND_ADDRESS = 0x004038E0

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
callers = {}

for ref in rm.getReferencesTo(space.getAddress(SEND_ADDRESS)):
    function = fm.getFunctionContaining(ref.getFromAddress())
    if function:
        callers[function.getEntryPoint().getOffset()] = function

matches = []
for entry in sorted(callers):
    function = callers[entry]
    result = decomp.decompileFunction(function, 120, monitor)
    if not result or not result.getDecompiledFunction():
        continue
    code = result.getDecompiledFunction().getC()
    if any(token in code.lower() for token in ("0x6a", "0x6b", "0x6c", "0x6d")):
        matches.append((entry, function, code))

with open(r"C:\temp\present_callbacks.txt", "w") as output:
    for entry, function, code in matches:
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("present callbacks: %d candidatos em %d callers" % (len(matches), len(callers)))
