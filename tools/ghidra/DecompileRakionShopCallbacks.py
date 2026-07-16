# -*- coding: utf-8 -*-
# Decompila callbacks Rakion próximos ao callback de move de inventário 0x31.
# Saída: C:\temp\rakion_shop_callbacks.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

START = 0x0047B000
END = 0x0047D1D0
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = []

iterator = manager.getFunctions(toAddr(START), True)
while iterator.hasNext():
    function = iterator.next()
    entry = function.getEntryPoint().getOffset()
    if entry > END:
        break
    functions.append(function)

with open(r"C:\temp\rakion_shop_callbacks.txt", "w") as output:
    for function in functions:
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("rakion shop callbacks: %d funcoes" % len(functions))
