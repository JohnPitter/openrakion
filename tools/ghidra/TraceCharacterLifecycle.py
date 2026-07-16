# -*- coding: utf-8 -*-
# Decompila o fluxo de delete/select no World original e os consumidores de erro no cliente.
# Saida: C:\temp\character_lifecycle_<programa>.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

name = currentProgram.getName().replace(".", "_")
output_path = r"C:\temp\character_lifecycle_%s.txt" % name
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
functions = {}

if "world" in name.lower():
    for address in (0x00412530, 0x0041FE10, 0x0041FEF0):
        function = fm.getFunctionAt(toAddr(address))
        if function:
            functions[function.getEntryPoint().getOffset()] = function

needles = (
    "Delete Character. Sever system error",
    "OnRecvCharacterDelete. Unknown error",
    "Select Character. Server system error",
    "Select Character. Character not exist",
    "Select Character. Unknown error",
)
hits = []
iterator = listing.getDefinedData(True)
while iterator.hasNext():
    data = iterator.next()
    value = data.getValue()
    if value is None:
        continue
    text = str(value)
    if not any(needle in text for needle in needles):
        continue
    refs = list(rm.getReferencesTo(data.getAddress()))
    hits.append((data, text, refs))
    for reference in refs:
        function = fm.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(output_path, "w") as output:
    output.write("=== strings ===\n")
    for data, text, refs in hits:
        output.write("%s %s\n" % (data.getAddress(), text))
        for reference in refs:
            output.write("  ref %s\n" % reference.getFromAddress())
    output.write("\n=== functions ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Character lifecycle: %d strings, %d funcoes -> %s" %
      (len(hits), len(functions), output_path))
