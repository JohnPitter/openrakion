# Decompila handlers, rotinas de banco e callbacks do inbox persistente de presentes.
# Saida: C:\temp\present_inbox_flow.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = [
    0x00416BF0,  # DB PresentPeek
    0x00416D90,  # DB PresentAccept
    0x004175E0,  # DB PresentDispose
    0x0041D650,  # notifica presentes criados
    0x004286A0,  # request PresentPeek
    0x00428750,  # request PresentAccept
    0x00428A10,  # request PresentDispose
]

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for offset in TARGETS:
    address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(offset)
    function = fm.getFunctionAt(address)
    if function:
        functions[function.getEntryPoint().getOffset()] = function
        for ref in rm.getReferencesTo(function.getEntryPoint()):
            caller = fm.getFunctionContaining(ref.getFromAddress())
            if caller:
                functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\present_inbox_flow.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decomp.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("present inbox: %d funcoes processadas" % len(functions))
