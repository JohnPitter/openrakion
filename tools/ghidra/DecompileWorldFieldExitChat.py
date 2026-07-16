# -*- coding: utf-8 -*-
# Extrai handlers World 0x46/0x47 e seus helpers diretos.
# Saída: C:\temp\world_field_exit_chat.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ENTRIES = (0x00424350, 0x004244F0)
fm = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for entry in ENTRIES:
    root = fm.getFunctionAt(toAddr(entry))
    functions[root.getEntryPoint().getOffset()] = root
    for called in root.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\world_field_exit_chat.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World FieldExit/Chat: %d funções" % len(functions))
