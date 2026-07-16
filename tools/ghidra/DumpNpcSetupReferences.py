# Decompila funcoes que referenciam a matriz runtime carregada de creatures.dat.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

NPC_SETUP_BASE = 0x353F2DB0

decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
target = space.getAddress(NPC_SETUP_BASE)


def decompile(function):
    result = decomp.decompileFunction(function, 90, monitor)
    print("\n===== %s @ %s =====" % (function.getName(), function.getEntryPoint()))
    if result and result.decompileCompleted():
        print(result.getDecompiledFunction().getC())
    else:
        print("(falha na decompilacao)")


seen = set()
found = []
for reference in references.getReferencesTo(target):
    function = functions.getFunctionContaining(reference.getFromAddress())
    if function is None:
        continue
    entry = function.getEntryPoint().getOffset()
    if entry in seen:
        continue
    seen.add(entry)
    found.append((function, reference.getFromAddress()))

instructions = currentProgram.getListing().getInstructions(True)
while instructions.hasNext():
    instruction = instructions.next()
    rendered = instruction.toString().lower()
    if not any(value in rendered for value in ("353f2db0", "351dd710", "351dd800")):
        continue
    function = functions.getFunctionContaining(instruction.getAddress())
    if function is None:
        continue
    entry = function.getEntryPoint().getOffset()
    if entry in seen:
        continue
    seen.add(entry)
    found.append((function, instruction.getAddress()))

print("PROGRAM=%s" % currentProgram.getName())
print("REFERENCE_FUNCTION_COUNT=%d" % len(found))
for function, source in found:
    print("REFERENCE=%s via %s" % (function.getName(), source))
    decompile(function)

print("##### DONE #####")
