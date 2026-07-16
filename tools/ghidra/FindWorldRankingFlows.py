# -*- coding: utf-8 -*-
# Localiza tabelas, SQL e consumidores de ranking no World original.
# Saida: C:\temp\world_ranking_flows.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

KEYWORDS = [
    "totalrank", "classrank", "clanrankp", "totalrankp", "swordmanrankp",
    "archerrankp", "blacksmithrankp", "ninjarankp", "magerankp", "lastrank"
]
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
strings = []

for data in listing.getDefinedData(True):
    value = str(data.getValue())
    if not any(keyword in value.lower() for keyword in KEYWORDS):
        continue
    strings.append((data.getAddress(), value))
    for reference in references.getReferencesTo(data.getAddress()):
        function = manager.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\world_ranking_flows.txt", "w") as output:
    output.write("=== strings ===\n")
    for address, value in strings:
        output.write("%s %s\n" %
                     (address, value.encode("ascii", "replace").decode("ascii")))
    output.write("\n=== consumidores ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("world ranking flows: %d strings, %d funcoes" %
      (len(strings), len(functions)))
