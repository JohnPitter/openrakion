# -*- coding: utf-8 -*-
# Extrai os builders C->S de voto 0x5D/0x5E.
# Saída: C:\temp\client_field_vote.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (0x361929D0, 0x36192A40, 0x36194360, 0x361943D0)
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

with open(r"C:\temp\client_field_vote.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client FieldVote: %d funções" % len(functions))
