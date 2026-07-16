# Decompila a faixa de handlers/callbacks dos produtos 10008..10014.
# Saida: C:\temp\stage_entitlement_callbacks.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = []

for function in manager.getFunctions(True):
    entry = function.getEntryPoint().getOffset()
    if 0x00428D00 <= entry <= 0x00429600:
        functions.append(function)

with open(r"C:\temp\stage_entitlement_callbacks.txt", "w") as output:
    for function in functions:
        entry = function.getEntryPoint().getOffset()
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("stage entitlements: %d funcoes processadas" % len(functions))
