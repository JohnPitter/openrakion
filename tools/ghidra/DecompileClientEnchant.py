# -*- coding: utf-8 -*-
# Decompila o export de refino e seus callers no engine.dll.
# Saída: C:\temp\client_enchant.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

NEEDLE = "SendInventoryEnchantReinforce"
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
function_manager = currentProgram.getFunctionManager()
reference_manager = currentProgram.getReferenceManager()
functions = {}

for function in function_manager.getFunctions(True):
    if NEEDLE.lower() not in function.getName(True).lower():
        continue
    functions[function.getEntryPoint().getOffset()] = function
    for reference in reference_manager.getReferencesTo(function.getEntryPoint()):
        caller = function_manager.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\client_enchant.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client enchant: %d funcoes" % len(functions))
