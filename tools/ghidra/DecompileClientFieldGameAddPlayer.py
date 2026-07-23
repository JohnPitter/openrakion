# -*- coding: utf-8 -*-
# Extrai todos os produtores de SendFieldGameAddPlayer na build v258.
# Saida: C:\temp\client_field_game_add_player_<programa>.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []
callers = {}

for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True).lower()
    if "sendfieldgameaddplayer" not in name:
        continue
    matches.append((symbol.getAddress(), symbol.getName(True)))
    for reference in references.getReferencesTo(symbol.getAddress()):
        caller = functions.getFunctionContaining(reference.getFromAddress())
        if caller:
            callers[caller.getEntryPoint().getOffset()] = caller

program_name = currentProgram.getName().lower().replace(".", "_")
output_path = r"C:\temp\client_field_game_add_player_%s.txt" % program_name
with open(output_path, "w") as output:
    output.write("=== symbols e referencias ===\n")
    for address, name in matches:
        output.write("%s %s\n" % (address, name))
        for reference in references.getReferencesTo(address):
            output.write("  %s %s\n" %
                         (reference.getFromAddress(), reference.getReferenceType()))
    for entry in sorted(callers):
        function = callers[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client FieldGameAddPlayer: %d simbolos, %d funcoes -> %s" %
      (len(matches), len(callers), output_path))
