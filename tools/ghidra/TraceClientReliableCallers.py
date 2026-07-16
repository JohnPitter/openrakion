# -*- coding: utf-8 -*-
# Extrai callers das APIs reliable para localizar tipos lógicos montados em runtime.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (0x361001C0, 0x36100BF0)
OUTPUT = r"C:\temp\client_reliable_callers.txt"

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()


def decompile(function):
    result = decompiler.decompileFunction(function, 240, monitor)
    if result and result.getDecompiledFunction():
        return result.getDecompiledFunction().getC()
    return "(falha)"


callers = {}
lines = []
for target_address in TARGETS:
    target = manager.getFunctionAt(toAddr(target_address))
    lines.append("===== TARGET %s @ %s =====\n" % (
        target.getName() if target else "(sem simbolo)", toAddr(target_address)))
    for reference in references.getReferencesTo(toAddr(target_address)):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        lines.append("%s <- %s @ %s\n" % (
            reference.getFromAddress(),
            caller.getName() if caller else "(sem funcao)",
            caller.getEntryPoint() if caller else "-"))
        if caller:
            callers[str(caller.getEntryPoint())] = caller

with open(OUTPUT, "w") as output:
    output.writelines(lines)
    for key in sorted(callers):
        function = callers[key]
        output.write("\n===== %s @ %s =====\n" % (
            function.getName(), function.getEntryPoint()))
        output.write(decompile(function).encode("ascii", "replace").decode("ascii"))

print("callers reliable do cliente extraidos")
