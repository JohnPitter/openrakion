# -*- coding: utf-8 -*-
# Rastreia a lista de demos e seus consumidores no executavel Rakion v258.
# Saida: C:\temp\rakion_demo_ui.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

FUNCTIONS = (0x00409800, 0x0040BE60, 0x0040DE90)
GLOBALS = (0x004FEEA0,)

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
references = []

for address in FUNCTIONS:
    function = fm.getFunctionAt(toAddr(address))
    if function:
        functions[function.getEntryPoint().getOffset()] = function
        for reference in rm.getReferencesTo(function.getEntryPoint()):
            caller = fm.getFunctionContaining(reference.getFromAddress())
            references.append(("function", function.getEntryPoint(), reference, caller))
            if caller:
                functions[caller.getEntryPoint().getOffset()] = caller

for address in GLOBALS:
    for reference in rm.getReferencesTo(toAddr(address)):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        references.append(("global", toAddr(address), reference, caller))
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

frontier = list(functions.values())
for _ in range(2):
    parents = []
    for function in frontier:
        for reference in rm.getReferencesTo(function.getEntryPoint()):
            caller = fm.getFunctionContaining(reference.getFromAddress())
            if caller and caller.getEntryPoint().getOffset() not in functions:
                functions[caller.getEntryPoint().getOffset()] = caller
                parents.append(caller)
    frontier = parents

with open(r"C:\temp\rakion_demo_ui.txt", "w") as output:
    output.write("=== references ===\n")
    for kind, target, reference, caller in references:
        source = reference.getFromAddress()
        output.write("%s target=%s source=%s caller=%s instruction=%s\n" % (
            kind, target, source, caller.getName(True) if caller else "-",
            listing.getInstructionAt(source) or "-"))
    output.write("\n=== decompiled ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Rakion demo UI: %d referencias, %d funcoes" % (len(references), len(functions)))
