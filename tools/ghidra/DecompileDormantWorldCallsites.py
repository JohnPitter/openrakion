# -*- coding: utf-8 -*-
# Audita calls virtuais cujos offsets coincidem com exports World rejeitados pelo servidor v258.
# Saida: C:\temp\client_dormant_world_callsites.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

SITES = (
    0x00408BB2, 0x0040EEF8, 0x004380C5, 0x0044E8C9, 0x0044E98D,
    0x0046AA7C, 0x00479586, 0x00489141, 0x0048914B, 0x0048956E,
    0x00489661, 0x0048A819, 0x0048AF84, 0x0048B21C, 0x0048C126,
)

listing = currentProgram.getListing()
fm = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for address in SITES:
    function = fm.getFunctionContaining(toAddr(address))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\client_dormant_world_callsites.txt", "w") as output:
    output.write("=== call sites ===\n")
    for address in SITES:
        function = fm.getFunctionContaining(toAddr(address))
        output.write("%08X function=%s instruction=%s\n" % (
            address, function.getName(True) if function else "-",
            listing.getInstructionAt(toAddr(address)) or "-"))
    output.write("\n=== decompiled ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Dormant World callsites: %d sites, %d funcoes" % (len(SITES), len(functions)))
