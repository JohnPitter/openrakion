# -*- coding: utf-8 -*-
# Localiza SQL, handlers e callbacks da loteria no World original.
# Saida: C:\temp\world_lottery_flows.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

KEYWORDS = [
    "lotto", "lottery", "loglottery", "dbcommandbuylotto",
    "dbcommandasklotto", "dbcommandlottoresult"
]
TARGETS = [0x0040EC50, 0x0040F0A0, 0x0040F2F0, 0x0041DFB0, 0x0041E0C0,
           0x004222A0, 0x004225D0]
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
strings = []

for target in TARGETS:
    function = manager.getFunctionContaining(toAddr(target))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

for data in listing.getDefinedData(True):
    value = str(data.getValue())
    if not any(keyword in value.lower() for keyword in KEYWORDS):
        continue
    strings.append((data.getAddress(), value))
    for reference in references.getReferencesTo(data.getAddress()):
        function = manager.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

for target in TARGETS:
    for reference in references.getReferencesTo(toAddr(target)):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\world_lottery_flows.txt", "w") as output:
    output.write("=== strings ===\n")
    for address, value in strings:
        output.write("%s %s\n" %
                     (address, value.encode("ascii", "replace").decode("ascii")))
    output.write("\n=== consumidores ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("world lottery flows: %d strings, %d funcoes" %
      (len(strings), len(functions)))
