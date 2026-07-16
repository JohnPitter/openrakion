# -*- coding: utf-8 -*-
# Localiza acessos aos campos da lista EFNMDemo no objeto principal do cliente.
# Saida: C:\temp\rakion_demo_state.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = (0x174, 0x43FC)
listing = currentProgram.getListing()
fm = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []
functions = {}

for instruction in listing.getInstructions(True):
    found = set()
    for operand in range(instruction.getNumOperands()):
        for item in instruction.getOpObjects(operand):
            if isinstance(item, Scalar) and item.getUnsignedValue() in TARGETS:
                found.add(item.getUnsignedValue())
    if not found:
        continue
    function = fm.getFunctionContaining(instruction.getAddress())
    matches.append((instruction, function, found))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\rakion_demo_state.txt", "w") as output:
    output.write("=== field accesses ===\n")
    for instruction, function, found in matches:
        output.write("%s offsets=%s function=%s instruction=%s\n" % (
            instruction.getAddress(), ",".join("0x%X" % value for value in sorted(found)),
            function.getName(True) if function else "-", instruction))
    output.write("\n=== decompiled ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Rakion demo state: %d acessos, %d funcoes" % (len(matches), len(functions)))
