# -*- coding: utf-8 -*-
# Decompila exports, callers e callees de Power User no engine.dll.
# Saida: C:\temp\client_power_user.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
roots = []

for function in fm.getFunctions(True):
    if "poweruser" not in function.getName(True).lower():
        continue
    roots.append(function)
    functions[function.getEntryPoint().getOffset()] = function
    for called in function.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called
    for reference in rm.getReferencesTo(function.getEntryPoint()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\client_power_user.txt", "w") as output:
    output.write("=== roots ===\n")
    for function in roots:
        output.write("%s @ %s\n" % (function.getName(True), function.getEntryPoint()))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client Power User: %d roots, %d funcoes" % (len(roots), len(functions)))
