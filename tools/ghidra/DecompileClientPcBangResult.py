# -*- coding: utf-8 -*-
# Decompila os consumidores runtime das texturas PC Bang no entitiesmp.dll.
# Saida: C:\temp\client_pcbang_result.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

FUNCTION_START = 0x351EA640
CALL_SITES = (0x351EAC33, 0x351EAC5D)
CONSUMER_SITES = (0x351EC7D8, 0x351ED83B)
fm = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

root = fm.getFunctionAt(toAddr(FUNCTION_START))
if root is None:
    disassemble(toAddr(FUNCTION_START))
    root = createFunction(toAddr(FUNCTION_START), None)
if root:
    functions[root.getEntryPoint().getOffset()] = root

for address in CALL_SITES:
    function = fm.getFunctionContaining(toAddr(address))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

for address in CONSUMER_SITES:
    function = fm.getFunctionContaining(toAddr(address))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

frontier = list(functions.values())
for _ in range(2):
    parents = []
    for function in frontier:
        for reference in rm.getReferencesTo(function.getEntryPoint()):
            caller = fm.getFunctionContaining(reference.getFromAddress())
            if caller and caller.getEntryPoint().getOffset() not in functions:
                functions[caller.getEntryPoint().getOffset()] = caller
                parents.append(caller)
    frontier = parents

with open(r"C:\temp\client_pcbang_result.txt", "w") as output:
    output.write("=== texture call sites ===\n")
    for address in CALL_SITES:
        instruction = listing.getInstructionAt(toAddr(address))
        function = fm.getFunctionContaining(toAddr(address))
        output.write("%08X instruction=%s function=%s\n" % (
            address, instruction or "-", function.getName(True) if function else "-"))
    output.write("=== texture consumer sites ===\n")
    for address in CONSUMER_SITES:
        instruction = listing.getInstructionAt(toAddr(address))
        function = fm.getFunctionContaining(toAddr(address))
        output.write("%08X instruction=%s function=%s\n" % (
            address, instruction or "-", function.getName(True) if function else "-"))
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client PC Bang result: %d funcoes" % len(functions))
