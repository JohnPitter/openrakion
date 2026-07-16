# Decompila os handlers de bag, Power User, character slot, potion slot e stage entitlements.
# Saida: C:\temp\inventory_entitlement_flows.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = [
    0x004226B0,  # 0x32 BuyBag
    0x00422B10,  # 0x34 BuyPowerUser
    0x00422850,  # 0x35 BuyCharacterSlot
    0x00428D80,  # 0x6F BuyPotionSlot
    0x004292B0,  # 0x70 BuyStageRankClear
    0x004293F0,  # 0x71 BuyStageLevelFree
]

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for offset in TARGETS:
    address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(offset)
    function = manager.getFunctionContaining(address)
    if not function:
        continue
    functions[function.getEntryPoint().getOffset()] = function
    for called in function.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called
        for nested in called.getCalledFunctions(monitor):
            functions[nested.getEntryPoint().getOffset()] = nested

with open(r"C:\temp\inventory_entitlement_flows.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("inventory entitlements: %d funcoes processadas" % len(functions))
