# -*- coding: utf-8 -*-
# Rastreia acessos ao slot de propriedade que controla o bonus PC Bang.
# Saida: C:\temp\client_pcbang_flag.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = (0x2D80, 0x2D84, 0x2D8C, 0x2D90)
name = currentProgram.getName().lower()
suffix = "rakion" if "rakion" in name else ("engine" if "engine" in name else "entities")
OUTPUT = r"C:\temp\client_pcbang_flag_%s.txt" % suffix

listing = currentProgram.getListing()
fm = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []
functions = {}

instructions = listing.getInstructions(True)
while instructions.hasNext():
    instruction = instructions.next()
    found = set()
    for index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(index):
            if hasattr(obj, "getValue"):
                value = obj.getValue()
                if value in TARGETS:
                    found.add(value)
    if not found:
        continue
    function = fm.getFunctionContaining(instruction.getAddress())
    if function:
        functions[function.getEntryPoint().getOffset()] = function
    matches.append((instruction.getAddress(), instruction, function, found))

with open(OUTPUT, "w") as output:
    output.write("=== acessos aos deslocamentos PC Bang ===\n")
    for address, instruction, function, found in matches:
        output.write("%s offsets=%s function=%s instruction=%s\n" % (
            address,
            ",".join("0x%X" % value for value in sorted(found)),
            function.getName(True) if function else "-",
            instruction))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client PC Bang flag: %d acessos em %d funcoes" %
      (len(matches), len(functions)))
