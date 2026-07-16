# -*- coding: utf-8 -*-
# Extrai os builders World SendEvent1/SendEvent4 e suas referencias imediatas.
# Saida: C:\temp\client_world_events.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
events = []
vtable = None

for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True).lower()
    if "iscavengerworldnet" in name and "vftable" in name:
        vtable = symbol.getAddress()
    if "sendevent1" not in name and "sendevent4" not in name:
        continue
    events.append((symbol.getAddress(), symbol.getName(True)))
    function = fm.getFunctionAt(symbol.getAddress())
    if function:
        functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\client_world_events.txt", "w") as output:
    output.write("=== symbols ===\n")
    for address, name in events:
        output.write("%s %s\n" % (address, name))
        for reference in rm.getReferencesTo(address):
            source = reference.getFromAddress()
            function = fm.getFunctionContaining(source)
            slot = source.subtract(vtable) if vtable and source.getAddressSpace() == vtable.getAddressSpace() else -1
            output.write("  ref=%s type=%s slot=%s caller=%s\n" % (
                source, reference.getReferenceType(),
                hex(slot) if slot >= 0 else "-",
                function.getName(True) if function else "-"))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client World events: %d simbolos, %d funcoes" % (len(events), len(functions)))
