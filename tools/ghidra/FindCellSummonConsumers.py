# Localiza propriedades/eventos textuais ligados a CP e summon e decompila consumidores diretos.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TOKENS = (
    "cp charge effect",
    "summon npc",
    "weapon cell",
    "set cell",
    "cp bomb",
    "cp item",
    "getcp",
    "reducecp",
    "checknpcspawned",
    "csummoner",
    "ccpeffect",
)

listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
consumers = {}

print("PROGRAM=%s" % currentProgram.getName())
for data in listing.getDefinedData(True):
    if not data.hasStringValue():
        continue
    value = str(data.getValue())
    if not any(token in value.lower() for token in TOKENS):
        continue
    refs = list(references.getReferencesTo(data.getAddress()))
    print("STRING=%s @ %s REFS=%d" % (value, data.getAddress(), len(refs)))
    for reference in refs:
        caller = functions.getFunctionContaining(reference.getFromAddress())
        print("  REF=%s CALLER=%s" % (
            reference.getFromAddress(), caller.getName() if caller else "-"))
        if caller:
            consumers[caller.getEntryPoint().getOffset()] = caller

print("CONSUMER_COUNT=%d" % len(consumers))
for entry in sorted(consumers):
    function = consumers[entry]
    result = decomp.decompileFunction(function, 90, monitor)
    print("\n===== %s @ %s =====" % (function.getName(), function.getEntryPoint()))
    if result and result.decompileCompleted():
        print(result.getDecompiledFunction().getC())
    else:
        print("(falha na decompilacao)")

print("##### DONE #####")
