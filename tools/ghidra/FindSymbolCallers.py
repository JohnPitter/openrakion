# -*- coding: utf-8 -*-
# Localiza simbolos por trecho de nome e decompila seus chamadores diretos.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


args = getScriptArgs()
if not args:
    raise ValueError("informe ao menos um trecho de nome")

needles = [value.lower() for value in args]
symbols = currentProgram.getSymbolTable()
references = currentProgram.getReferenceManager()
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []
functions = {}

for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True)
    if not any(needle in name.lower() for needle in needles):
        continue
    matches.append(symbol)
    for reference in references.getReferencesTo(symbol.getAddress()):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        if caller is not None:
            functions[str(caller.getEntryPoint())] = caller

print("PROGRAM=%s SYMBOLS=%d CALLERS=%d" % (
    currentProgram.getName(), len(matches), len(functions)))
for symbol in matches:
    print("SYMBOL %s @ %s" % (symbol.getName(True), symbol.getAddress()))
    for reference in references.getReferencesTo(symbol.getAddress()):
        print("  REF %s %s" % (reference.getFromAddress(), reference.getReferenceType()))

for key in sorted(functions):
    function = functions[key]
    print("\n===== %s @ %s =====" % (function.getName(), function.getEntryPoint()))
    result = decompiler.decompileFunction(function, 240, monitor)
    if result and result.getDecompiledFunction():
        print(result.getDecompiledFunction().getC())

print("##### DONE #####")
