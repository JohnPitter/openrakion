# -*- coding: utf-8 -*-
# Extrai o handler World do opcode 0x64 e seus helpers imediatos.
# Saida: C:\temp\world_gm_operation.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ENTRY = 0x004283A0
fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
root = fm.getFunctionAt(toAddr(ENTRY))
functions = {root.getEntryPoint().getOffset(): root}

for called in root.getCalledFunctions(monitor):
    functions[called.getEntryPoint().getOffset()] = called

table_references = []
for offset in range(0, 16, 4):
    address = toAddr(0x00456034 + offset)
    for reference in rm.getReferencesTo(address):
        table_references.append(reference)
        function = fm.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\world_gm_operation.txt", "w") as output:
    output.write("=== refs da tabela @ 00456034..00456043 ===\n")
    for reference in table_references:
        output.write("%s -> %s %s\n" % (reference.getFromAddress(),
                     reference.getToAddress(), reference.getReferenceType()))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World GMOperation: %d funcoes" % len(functions))
