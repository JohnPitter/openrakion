# -*- coding: utf-8 -*-
# Decompila os exports de compra/venda geral do inventário no engine.dll.
# Saída: C:\temp\client_shop_purchase.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

needles = [
    "SendInventoryBuy",
    "SendInventorySell",
]

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []
callers = {}
references = []

for function in currentProgram.getFunctionManager().getFunctions(True):
    name = function.getName(True)
    if any(needle.lower() in name.lower() for needle in needles):
        matches.append(function)

reference_manager = currentProgram.getReferenceManager()
function_manager = currentProgram.getFunctionManager()
for function in matches:
    for reference in reference_manager.getReferencesTo(function.getEntryPoint()):
        references.append((function, reference))
        caller = function_manager.getFunctionContaining(reference.getFromAddress())
        if caller and caller.getEntryPoint() != function.getEntryPoint():
            callers[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\client_shop_purchase.txt", "w") as output:
    for function in matches:
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))
    output.write("\n=== referências ===\n")
    for target, reference in references:
        output.write("%s <- %s %s\n" %
                     (target.getName(True), reference.getFromAddress(), reference.getReferenceType()))
    output.write("\n=== callers ===\n")
    for entry in sorted(callers):
        function = callers[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client shop: %d exports, %d callers" % (len(matches), len(callers)))
