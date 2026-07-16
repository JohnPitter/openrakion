# -*- coding: utf-8 -*-
# Extrai os callers dos handlers UDP para identificar o envelope removido antes do payload.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = (0x00425D80, 0x00425FA0)
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

callers = {}
for target in TARGETS:
    for reference in references.getReferencesTo(toAddr(target)):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        if caller is not None:
            callers[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\world_udp_handshake_callers.txt", "w") as output:
    for entry in sorted(callers):
        function = callers[entry]
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("callers UDP extraidos: %d" % len(callers))
