# -*- coding: utf-8 -*-
# Localiza consumidores de ItemSetup que também acessam estado de equipamento/atributos.
# Saída: C:\temp\client_equipment_stat_consumers.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for symbol in symbols.getAllSymbols(True):
    if "getiteminfo" not in symbol.getName(True).lower():
        continue
    for reference in references.getReferencesTo(symbol.getAddress()):
        function = manager.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

terms = ("0xbc", "0x3268", "0x2e44", "0x1218", "0x1dca", "0x2a")
matches = []
for entry in sorted(functions):
    function = functions[entry]
    result = decompiler.decompileFunction(function, 180, monitor)
    code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else ""
    lowered = code.lower()
    if any(term in lowered for term in terms):
        matches.append((function, code))

with open(r"C:\temp\client_equipment_stat_consumers.txt", "w") as output:
    output.write("direct_getiteminfo_callers=%d matched=%d\n" % (len(functions), len(matches)))
    for function, code in matches:
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client equipment stat consumers: %d/%d" % (len(matches), len(functions)))
