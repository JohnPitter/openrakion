# -*- coding: utf-8 -*-
# Localiza consumidores dos textos 829..864 da UI de loteria no rakion.bin.
# Saida: C:\temp\client_lottery_ui.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

LANGUAGE_IDS = set(range(829, 865))
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
hits = []

for instruction in listing.getInstructions(True):
    for operand in range(instruction.getNumOperands()):
        for item in instruction.getOpObjects(operand):
            if isinstance(item, Scalar) and item.getUnsignedValue() in LANGUAGE_IDS:
                function = manager.getFunctionContaining(instruction.getAddress())
                if function:
                    functions[function.getEntryPoint().getOffset()] = function
                    hits.append((instruction.getAddress(), item.getUnsignedValue(), function.getName()))

with open(r"C:\temp\client_lottery_ui.txt", "w") as output:
    output.write("=== language-id hits ===\n")
    for address, value, name in hits:
        output.write("%s id=%d function=%s\n" % (address, value, name))
    output.write("\n=== consumidores ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client lottery UI: %d hits, %d funcoes" % (len(hits), len(functions)))
