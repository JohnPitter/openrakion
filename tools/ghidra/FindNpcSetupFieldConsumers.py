# Localiza consumidores de offsets do registro runtime NpcSetup.
# @category Rakion
import re

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ACCESSORS = (0x350DCC20, 0x350E1770)

raw_offsets = getScriptArgs() or ("0x8c",)
patterns = []
for raw in raw_offsets:
    value = int(raw, 0)
    forms = (raw.lower(), str(value))
    patterns.append(re.compile(r"\+\s*(?:%s)\b" % "|".join(
        re.escape(form) for form in forms)))
    if value % 4 == 0:
        patterns.append(re.compile(r"\[\s*(?:0x%x|%d)\s*\]" % (value // 4, value // 4)))
patterns = tuple(patterns)
functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

callers = {}
for accessor in ACCESSORS:
    for reference in references.getReferencesTo(space.getAddress(accessor)):
        caller = functions.getFunctionContaining(reference.getFromAddress())
        if caller is not None:
            callers[caller.getEntryPoint().getOffset()] = caller

matches = []
for entry in sorted(callers):
    function = callers[entry]
    result = decomp.decompileFunction(function, 90, monitor)
    if not result or not result.decompileCompleted():
        continue
    code = result.getDecompiledFunction().getC()
    lowered = code.lower()
    if any(pattern.search(lowered) for pattern in patterns):
        matches.append((function, code))

print("PROGRAM=%s OFFSETS=%s CALLERS=%d MATCHES=%d" % (
    currentProgram.getName(), ",".join(raw_offsets), len(callers), len(matches)))
for function, code in matches:
    print("\n===== %s @ %s =====" % (function.getName(), function.getEntryPoint()))
    print(code)

print("##### DONE #####")
