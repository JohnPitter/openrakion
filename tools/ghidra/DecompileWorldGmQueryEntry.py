# -*- coding: utf-8 -*-
# Extrai GmQueryEntry 0x09, o serializer e o inicializador do field.
# Saida: C:\temp\world_gm_query_entry.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ENTRIES = (0x00405440, 0x004058E0, 0x0041F5C0)
functions = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_gm_query_entry.txt", "w") as output:
    for entry in ENTRIES:
        function = functions.getFunctionAt(toAddr(entry))
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() \
            if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World GmQueryEntry: %d funcoes" % len(ENTRIES))
