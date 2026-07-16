# -*- coding: utf-8 -*-
# Extrai handlers 0x5D/0x5E e o agregado compartilhado de votação.
# Saída: C:\temp\world_field_vote.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (0x00425A70, 0x00425BB0, 0x0040A420, 0x004098E0, 0x00409810,
             0x004090B0, 0x004068C0)
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

with open(r"C:\temp\world_field_vote.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World FieldVote: %d funções" % len(functions))
