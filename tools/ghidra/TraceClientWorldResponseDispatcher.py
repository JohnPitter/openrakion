# -*- coding: utf-8 -*-
# Rastreia referencias e callers do dispatcher da fila IScavengerWorldNet S->C.
# Saida: C:\temp\client_world_response_dispatcher_refs.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


DISPATCHER = 0x36197320
OUTPUT = r"C:\temp\client_world_response_dispatcher_refs.txt"

functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

target = toAddr(DISPATCHER)
function = functions.getFunctionAt(target)
if function is None:
    raise ValueError("dispatcher ausente em 0x%08X" % DISPATCHER)

rows = []
callers = {}
for reference in references.getReferencesTo(target):
    source = reference.getFromAddress()
    caller = functions.getFunctionContaining(source)
    if caller:
        callers[caller.getEntryPoint().getOffset()] = caller
    primary = symbols.getPrimarySymbol(source)
    rows.append((source, reference.getReferenceType(), caller, primary,
                 listing.getInstructionAt(source)))

with open(OUTPUT, "w") as output:
    output.write("target=%s function=%s\n" % (target, function.getName(True)))
    output.write("references=%d callers=%d\n" % (len(rows), len(callers)))
    for source, ref_type, caller, primary, instruction in rows:
        output.write("from=%s type=%s symbol=%s caller=%s instruction=%s\n" % (
            source, ref_type, primary.getName(True) if primary else "-",
            caller.getName(True) if caller else "-", instruction or "-"))
    for entry in sorted(callers):
        caller = callers[entry]
        result = decompiler.decompileFunction(caller, 240, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (caller.getName(True), caller.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("referencias do dispatcher S->C: %d, callers: %d" %
      (len(rows), len(callers)))
