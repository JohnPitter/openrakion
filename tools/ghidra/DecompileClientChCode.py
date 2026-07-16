# -*- coding: utf-8 -*-
# Localiza SendChCode/SendPacketSpeedTest no engine e decompila exports e referencias diretas.
# Saida: C:\temp\client_ch_code.txt
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
for symbol in currentProgram.getSymbolTable().getAllSymbols(True):
    name = symbol.getName(True).lower()
    if "sendchcode" not in name and "sendpacketspeedtest" not in name:
        continue
    symbols.append((symbol.getAddress(), symbol.getName(True)))
    function = fm.getFunctionAt(symbol.getAddress())
    if function:
        functions[function.getEntryPoint().getOffset()] = function
    for reference in rm.getReferencesTo(symbol.getAddress()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

roots = list(functions.values())
for function in roots:
    for reference in rm.getReferencesTo(function.getEntryPoint()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\client_ch_code.txt", "w") as output:
    output.write("=== symbols ===\n")
    for address, name in symbols:
        output.write("%s %s\n" % (address, name))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client ChCode: %d simbolos, %d funcoes" % (len(symbols), len(functions)))
