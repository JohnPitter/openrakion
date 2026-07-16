# Localiza callers/registradores dos comandos DB de rank-clear e stage-level-free.
# Saida: C:\temp\stage_entitlement_db_callers.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = [0x00417F10, 0x004184A0]
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
hits = []

for target in TARGETS:
    address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(target)
    for reference in references.getReferencesTo(address):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        hits.append((target, reference.getFromAddress(), caller.getName() if caller else "-"))
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\stage_entitlement_db_callers.txt", "w") as output:
    output.write("=== referencias ===\n")
    for target, source, name in hits:
        output.write("%08x <- %s %s\n" % (target, source, name))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("stage entitlement DB callers: %d referencias, %d funcoes" % (len(hits), len(functions)))
