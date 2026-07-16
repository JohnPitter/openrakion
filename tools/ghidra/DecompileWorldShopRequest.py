# -*- coding: utf-8 -*-
# Decompila os handlers 0x2E/0x2F e as rotinas diretas chamadas por eles no World original.
# Saída: C:\temp\world_shop_request.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = (0x0040A810, 0x0040BCB0, 0x00421210, 0x004215A0)
function_manager = currentProgram.getFunctionManager()
reference_manager = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

functions = {}
for target_address in TARGETS:
    target = function_manager.getFunctionContaining(toAddr(target_address))
    if not target:
        continue
    functions[target.getEntryPoint().getOffset()] = target
    for address in target.getBody().getAddresses(True):
        for reference in reference_manager.getReferencesFrom(address):
            called = function_manager.getFunctionAt(reference.getToAddress())
            if called:
                functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\world_shop_request.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("world shop request: %d funcoes" % len(functions))
