# -*- coding: utf-8 -*-
# Localiza referencias de codigo aos assets de evento/Valentine no modulo atual.
# Saida: C:\temp\client_event_assets_<modulo>.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TOKENS = (
    "valentine", "eventtime", "eventitem", "eventround",
    "img_event", "name_heart", "eventlogo", "pcbang",
)

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []
callers = {}

for data in listing.getDefinedData(True):
    if not data.hasStringValue():
        continue
    value = str(data.getValue())
    if not any(token in value.lower() for token in TOKENS):
        continue
    references = list(rm.getReferencesTo(data.getAddress()))
    matches.append((data.getAddress(), value, references))
    for reference in references:
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            callers[caller.getEntryPoint().getOffset()] = caller

frontier = list(callers.values())
for _ in range(2):
    parents = []
    for function in frontier:
        for reference in rm.getReferencesTo(function.getEntryPoint()):
            caller = fm.getFunctionContaining(reference.getFromAddress())
            if caller and caller.getEntryPoint().getOffset() not in callers:
                callers[caller.getEntryPoint().getOffset()] = caller
                parents.append(caller)
    frontier = parents

module_name = currentProgram.getName().lower().replace(".dll", "").replace(".bin", "")
output_path = r"C:\temp\client_event_assets_%s.txt" % module_name
with open(output_path, "w") as output:
    output.write("=== strings and references ===\n")
    for address, value, references in matches:
        output.write("%s %s\n" % (address, value.encode("ascii", "replace").decode("ascii")))
        for reference in references:
            source = reference.getFromAddress()
            caller = fm.getFunctionContaining(source)
            output.write("  ref=%s type=%s caller=%s instruction=%s\n" % (
                source, reference.getReferenceType(),
                caller.getName(True) if caller else "-",
                listing.getInstructionAt(source) or "-"))

    output.write("\n=== decompiled consumers ===\n")
    for entry in sorted(callers):
        function = callers[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client event assets: %d strings, %d consumidores, saida=%s" %
      (len(matches), len(callers), output_path))
