# -*- coding: utf-8 -*-
# Extrai os consumidores do protocolo AutoFetch do NyxLauncher.
# Saida: C:\temp\nyx_autofetch.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
needles = ("DoFetch", "CAutoFetch", "COMMAND", "Replacer.exe", "File Download")
functions = {}
strings = []

data = listing.getDefinedData(True)
while data.hasNext():
    item = data.next()
    value = item.getValue()
    text = unicode(value).encode("ascii", "ignore") if value is not None else ""
    if not any(needle.lower() in text.lower() for needle in needles):
        continue
    strings.append((item.getAddress(), text))
    for reference in rm.getReferencesTo(item.getAddress()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

for function in list(functions.values()):
    for called in function.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\nyx_autofetch.txt", "w") as output:
    output.write("=== strings ===\n")
    for address, text in strings:
        output.write("%s %s\n" % (address, text))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Nyx AutoFetch: %d strings, %d funcoes" % (len(strings), len(functions)))
