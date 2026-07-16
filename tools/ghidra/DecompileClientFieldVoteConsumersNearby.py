# -*- coding: utf-8 -*-
# Extrai consumidores S->C vizinhos ao dispatcher de callbacks do engine.
# Saida: C:\temp\client_field_vote_consumers_nearby.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

START = toAddr(0x36192a80)
END = toAddr(0x36194080)
functions = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\client_field_vote_consumers_nearby.txt", "w") as output:
    iterator = functions.getFunctions(START, True)
    count = 0
    while iterator.hasNext():
        function = iterator.next()
        if function.getEntryPoint().compareTo(END) >= 0:
            break
        result = decompiler.decompileFunction(function, 120, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))
        count += 1

print("consumidores proximos: %d funcoes" % count)
