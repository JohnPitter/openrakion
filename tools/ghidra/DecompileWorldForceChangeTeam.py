# -*- coding: utf-8 -*-
# Extrai handler 0x5B, mutação do target e helpers diretos.
# Saída: C:\temp\world_force_change_team.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (0x00425990, 0x00409080)
fm = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for address in ADDRESSES:
    root = fm.getFunctionAt(toAddr(address))
    if not root:
        continue
    functions[root.getEntryPoint().getOffset()] = root
    for called in root.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\world_force_change_team.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World ForceChangeTeam: %d funções" % len(functions))
