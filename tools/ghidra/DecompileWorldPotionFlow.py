# -*- coding: utf-8 -*-
# Extrai o handler 0x6E, o validador de uso de poção e seus helpers diretos.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (0x00428C90, 0x0040E5F0)
OUTPUT = r"C:\temp\world_potion_flow.txt"

manager = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for offset in TARGETS:
    function = manager.getFunctionAt(toAddr(offset))
    if function is not None:
        functions[str(function.getEntryPoint())] = function

helper = manager.getFunctionAt(toAddr(0x0040E5F0))
if helper is not None:
    instructions = listing.getInstructions(helper.getBody(), True)
    while instructions.hasNext():
        instruction = instructions.next()
        for flow in instruction.getFlows():
            callee = manager.getFunctionAt(flow)
            if callee is not None:
                functions[str(callee.getEntryPoint())] = callee

with open(OUTPUT, "w") as output:
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("fluxo de pocoes extraido em " + OUTPUT)
