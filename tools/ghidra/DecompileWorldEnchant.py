# -*- coding: utf-8 -*-
# Decompila preview, commit e regra de refino no World original.
# Saída: C:\temp\world_enchant.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = [0x00421E10, 0x0041DE40, 0x0040C310, 0x0042A810]
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
function_manager = currentProgram.getFunctionManager()
reference_manager = currentProgram.getReferenceManager()
functions = {}

for target in TARGETS:
    function = function_manager.getFunctionContaining(toAddr(target))
    if not function:
        continue
    functions[function.getEntryPoint().getOffset()] = function
    for address in function.getBody().getAddresses(True):
        for reference in reference_manager.getReferencesFrom(address):
            called = function_manager.getFunctionAt(reference.getToAddress())
            if called:
                functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\world_enchant.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("world enchant: %d funcoes" % len(functions))
