# -*- coding: utf-8 -*-
# Decompila todos os callers do accessor WorldNet usado pela UI do rakion.bin.
# Saida: C:\temp\rakion_worldnet_accessor.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ACCESSOR = 0x00471B70
fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
function = fm.getFunctionAt(toAddr(ACCESSOR))
callers = {}
references = []

if function:
    for reference in rm.getReferencesTo(function.getEntryPoint()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        references.append((reference, caller))
        if caller:
            callers[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\rakion_worldnet_accessor.txt", "w") as output:
    output.write("=== references ===\n")
    for reference, caller in references:
        source = reference.getFromAddress()
        output.write("%s caller=%s instruction=%s\n" % (
            source, caller.getName(True) if caller else "-",
            listing.getInstructionAt(source) or "-"))
    output.write("\n=== decompiled callers ===\n")
    for entry in sorted(callers):
        caller = callers[entry]
        result = decompiler.decompileFunction(caller, 240, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (caller.getName(True), caller.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Rakion WorldNet accessor: %d referencias, %d callers" %
      (len(references), len(callers)))
