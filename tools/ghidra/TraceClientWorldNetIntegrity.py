# -*- coding: utf-8 -*-
# Rastreia consumidores reais de _pRakionWorldNet e destaca chamadas nos slots
# virtuais de SendPacketSpeedTest (0x144) e SendChCode (0x150).
# Saida: C:\temp\client_worldnet_integrity_<modulo>.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
symbol_table = currentProgram.getSymbolTable()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

world_symbols = []
callers = {}

for symbol in symbol_table.getAllSymbols(True):
    normalized = symbol.getName(True).lower()
    if "prakionworldnet" not in normalized:
        continue
    world_symbols.append(symbol)
    for reference in rm.getReferencesTo(symbol.getAddress()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            callers[caller.getEntryPoint().getOffset()] = caller

module_name = currentProgram.getName().lower().replace(".dll", "").replace(".bin", "")
output_path = r"C:\temp\client_worldnet_integrity_%s.txt" % module_name

with open(output_path, "w") as output:
    output.write("=== _pRakionWorldNet symbols and references ===\n")
    for symbol in world_symbols:
        output.write("%s %s\n" % (symbol.getAddress(), symbol.getName(True)))
        for reference in rm.getReferencesTo(symbol.getAddress()):
            source = reference.getFromAddress()
            caller = fm.getFunctionContaining(source)
            instruction = listing.getInstructionAt(source)
            output.write("  ref=%s type=%s caller=%s instruction=%s\n" % (
                source,
                reference.getReferenceType(),
                caller.getName(True) if caller else "-",
                instruction if instruction else "-"))

    output.write("\n=== decompiled consumers ===\n")
    for entry in sorted(callers):
        function = callers[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        decompiled = (result.getDecompiledFunction().getC()
                      if result and result.getDecompiledFunction() else "(falha)")
        normalized = decompiled.replace(" ", "").lower()
        markers = []
        for slot in ("+0x144", "+324", "+0x150", "+336"):
            if slot in normalized:
                markers.append(slot)
        output.write("\n===== %s @ %s markers=%s =====\n" % (
            function.getName(True), function.getEntryPoint(),
            ",".join(markers) if markers else "-"))
        output.write(decompiled.encode("ascii", "replace").decode("ascii"))

print("Client WorldNet integrity: %d simbolos, %d consumidores, saida=%s" %
      (len(world_symbols), len(callers), output_path))
