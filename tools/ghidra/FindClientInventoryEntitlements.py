# -*- coding: utf-8 -*-
# Decompila exports de compra de slots e funções que referenciam strings relacionadas no engine.dll.
# Saida: C:\temp\client_inventory_entitlements.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

NAMES = [
    "SendInventoryBuyBag",
    "SendInventoryBuyCharacterSlot",
    "SendInventoryBuyPotionSlot",
    "SendInventoryBuyStageRankClear",
    "SendInventoryBuyStageLevelFree",
]

manager = currentProgram.getFunctionManager()
symbols = currentProgram.getSymbolTable()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for function in manager.getFunctions(True):
    name = function.getName()
    if any(target in name for target in NAMES):
        functions[function.getEntryPoint().getOffset()] = function

for data in currentProgram.getListing().getDefinedData(True):
    value = str(data.getValue())
    if "InventoryBuy" not in value and "PotionSlot" not in value:
        continue
    for ref in references.getReferencesTo(data.getAddress()):
        function = manager.getFunctionContaining(ref.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\client_inventory_entitlements.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client inventory entitlements: %d funcoes processadas" % len(functions))
