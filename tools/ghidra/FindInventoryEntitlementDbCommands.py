# -*- coding: utf-8 -*-
# Localiza comandos SQL e callbacks de entitlements por strings no World original.
# Saida: C:\temp\inventory_entitlement_db_commands.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

KEYWORDS = [
    "potionslot", "stagelevelfree", "buycharacterslot", "buybag",
    "set bag", "set slot", "logbuycashitem"
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
    lowered = value.lower()
    if not any(keyword in lowered for keyword in KEYWORDS):
        continue
    strings.append((data.getAddress(), value))
    for ref in references.getReferencesTo(data.getAddress()):
        function = manager.getFunctionContaining(ref.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\inventory_entitlement_db_commands.txt", "w") as output:
    output.write("=== strings ===\n")
    for address, value in strings:
        output.write("%s %s\n" % (address, value.encode("ascii", "replace").decode("ascii")))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("inventory db commands: %d strings, %d funcoes" % (len(strings), len(functions)))
