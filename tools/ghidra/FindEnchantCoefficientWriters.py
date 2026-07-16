# -*- coding: utf-8 -*-
# Localiza leitores/escritores das tabelas de coeficientes usadas por FUN_0040C310.
# Saída: C:\temp\world_enchant_coefficients.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

NEEDLES = ["0x52e0", "0x5314", "0x7844", "0x7810", "13001", "0x32c9"]
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []

for function in currentProgram.getFunctionManager().getFunctions(True):
    result = decompiler.decompileFunction(function, 90, monitor)
    if not result or not result.getDecompiledFunction():
        continue
    code = result.getDecompiledFunction().getC()
    lowered = code.lower()
    if any(needle in lowered for needle in NEEDLES):
        matches.append((function, code))

with open(r"C:\temp\world_enchant_coefficients.txt", "w") as output:
    for function, code in matches:
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("world enchant coefficients: %d funcoes" % len(matches))
