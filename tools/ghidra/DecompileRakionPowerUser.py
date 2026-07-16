# -*- coding: utf-8 -*-
# Localiza consumidores de Power User e mensagens associadas no rakion.bin.
# Saida: C:\temp\rakion_power_user.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
symbols = []
strings = []

for symbol in currentProgram.getSymbolTable().getAllSymbols(True):
    if "poweruser" not in symbol.getName(True).lower():
        continue
    symbols.append((symbol.getAddress(), symbol.getName(True)))
    for reference in rm.getReferencesTo(symbol.getAddress()):
        function = fm.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

for data in currentProgram.getListing().getDefinedData(True):
    value = data.getValue()
    if value is None or "power user" not in str(value).lower():
        continue
    strings.append((data.getAddress(), str(value)))
    for reference in rm.getReferencesTo(data.getAddress()):
        function = fm.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

roots = list(functions.values())
for function in roots:
    for called in function.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called
    for reference in rm.getReferencesTo(function.getEntryPoint()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\rakion_power_user.txt", "w") as output:
    output.write("=== symbols ===\n")
    for address, name in symbols:
        output.write("%s %s\n" % (address, name))
    output.write("\n=== strings ===\n")
    for address, value in strings:
        output.write("%s %s\n" % (address, value))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("rakion Power User: %d symbols, %d strings, %d funcoes" %
      (len(symbols), len(strings), len(functions)))
