# Decompila os chamadores dos enderecos passados como argumentos hexadecimais.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()

print("PROGRAM=%s" % currentProgram.getName())
for raw in getScriptArgs():
    target = space.getAddress(int(raw, 0))
    seen = set()
    callers = []
    for reference in references.getReferencesTo(target):
        function = functions.getFunctionContaining(reference.getFromAddress())
        if function is None:
            continue
        entry = function.getEntryPoint().getOffset()
        if entry in seen:
            continue
        seen.add(entry)
        callers.append((function, reference.getFromAddress()))
    print("\nTARGET=%s CALLER_COUNT=%d" % (target, len(callers)))
    for function, source in callers:
        result = decomp.decompileFunction(function, 90, monitor)
        print("\n===== %s @ %s via %s =====" % (
            function.getName(), function.getEntryPoint(), source))
        if result and result.decompileCompleted():
            print(result.getDecompiledFunction().getC())
        else:
            print("(falha na decompilacao)")

print("##### DONE #####")
