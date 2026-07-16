# -*- coding: utf-8 -*-
# Extrai o transporte genérico de eventos de entidade usado pelo combate.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (0x36128A90,)
OUTPUT = r"C:\temp\engine_entity_events.txt"

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for target in TARGETS:
        function = manager.getFunctionAt(toAddr(target))
        output.write("===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        output.write(result.getDecompiledFunction().getC())

print("transporte de eventos de entidade extraido em " + OUTPUT)
