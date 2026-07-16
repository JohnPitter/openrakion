# Localiza leituras/escritas dos campos de bag, character slot, potion slot e stage entitlement.
# Saida: C:\temp\inventory_entitlement_callbacks.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

OFFSETS = set([0x144C, 0x1531, 0x1540, 0x1541, 0x2368])
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
hits = []

for instruction in listing.getInstructions(True):
    matched = []
    for index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(index):
            if isinstance(obj, Scalar) and (obj.getUnsignedValue() & 0xffffffff) in OFFSETS:
                matched.append(obj.getUnsignedValue() & 0xffffffff)
    if not matched:
        continue
    function = manager.getFunctionContaining(instruction.getAddress())
    hits.append((instruction.getAddress(), str(instruction), matched))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\inventory_entitlement_callbacks.txt", "w") as output:
    output.write("=== instrucoes ===\n")
    for address, instruction, matched in hits:
        output.write("%s %s offsets=%s\n" % (address, instruction, matched))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("inventory callbacks: %d instrucoes, %d funcoes" % (len(hits), len(functions)))
