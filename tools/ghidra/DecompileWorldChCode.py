# -*- coding: utf-8 -*-
# Extrai o handler World do opcode 0x65 e seus helpers imediatos.
# Saida: C:\temp\world_ch_code.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ENTRY = 0x00428430
fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
root = fm.getFunctionAt(toAddr(ENTRY))
functions = {root.getEntryPoint().getOffset(): root}

for called in root.getCalledFunctions(monitor):
    functions[called.getEntryPoint().getOffset()] = called

for offset in (0x12C, 0x14D, 0x1460, 0x237C):
    scalar = long(offset)
    for instruction in currentProgram.getListing().getInstructions(True):
        for operand in range(instruction.getNumOperands()):
            for representation in instruction.getOpObjects(operand):
                if hasattr(representation, "getValue") and representation.getValue() == scalar:
                    function = fm.getFunctionContaining(instruction.getAddress())
                    if function:
                        functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\world_ch_code.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World ChCode: %d funcoes" % len(functions))
