# -*- coding: utf-8 -*-
# Extrai builders e referências dos exports relacionados a resultado 0x50..0x53.
# Saída: C:\temp\client_game_point_<programa>.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

NEEDLES = (
    "sendfieldgamepoint",
    "sendfieldgamestagepoint",
    "sendfieldslotudp",
)

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
matches = []

for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True).lower()
    if not any(needle in name for needle in NEEDLES):
        continue
    matches.append((symbol.getAddress(), symbol.getName(True)))
    function = fm.getFunctionAt(symbol.getAddress())
    if function:
        functions[function.getEntryPoint().getOffset()] = function
    for reference in rm.getReferencesTo(symbol.getAddress()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

program_name = currentProgram.getName().lower().replace(".", "_")
output_path = r"C:\temp\client_game_point_%s.txt" % program_name
with open(output_path, "w") as output:
    output.write("=== symbols e referências ===\n")
    for address, name in matches:
        output.write("%s %s\n" % (address, name))
        for reference in rm.getReferencesTo(address):
            output.write("  %s %s\n" % (reference.getFromAddress(), reference.getReferenceType()))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client GamePoint: %d símbolos, %d funções -> %s" %
      (len(matches), len(functions), output_path))
