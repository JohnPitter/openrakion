# -*- coding: utf-8 -*-
# Localiza no World original as rotinas SQL de compra, venda e catálogo da loja.
# Saída: C:\temp\world_shop_economy.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

needles = [
    "LogBuyCashItem",
    "LogUserItem",
    "DELETE FROM useriteminfo WHERE id=%u",
    "UPDATE usergameinfo SET gold=gold+%u",
    "DBCommandItemSell",
    "DBCommandInventorySell",
    "buyinfo",
]

functions = {}
hits = []
fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

for data in currentProgram.getListing().getDefinedData(True):
    value = data.getValue()
    if value is None:
        continue
    text = str(value)
    if not any(needle.lower() in text.lower() for needle in needles):
        continue
    hits.append((data.getAddress(), text))
    for reference in rm.getReferencesTo(data.getAddress()):
        function = fm.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\world_shop_economy.txt", "w") as output:
    output.write("=== strings encontradas ===\n")
    for address, value in hits:
        output.write("%s %s\n" % (address, value))
    output.write("\n=== consumidores diretos ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("shop economy: %d strings, %d funcoes" % (len(hits), len(functions)))
