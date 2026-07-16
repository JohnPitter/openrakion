# -*- coding: utf-8 -*-
# Localiza comandos SQL e callbacks de Power User no World original.
# Saida: C:\temp\world_power_user_callbacks.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

NEEDLES = [
    "logbuypoweruser",
    "powerlevelpoint_cur",
    "dbcommandbuypoweruser",
    "buy power user",
]

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
hits = []

for data in currentProgram.getListing().getDefinedData(True):
    value = data.getValue()
    if value is None:
        continue
    value_text = str(value)
    if not any(needle in value_text.lower() for needle in NEEDLES):
        continue
    hits.append((data.getAddress(), value_text))
    for reference in rm.getReferencesTo(data.getAddress()):
        function = fm.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

roots = list(functions.values())
for function in roots:
    for called in function.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called
    for reference in rm.getReferencesTo(function.getEntryPoint()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\world_power_user_callbacks.txt", "w") as output:
    output.write("=== strings ===\n")
    for address, value in hits:
        output.write("%s %s\n" % (address, value))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World Power User: %d strings, %d funcoes" % (len(hits), len(functions)))
