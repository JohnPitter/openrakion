# -*- coding: utf-8 -*-
# Extrai o handler C->S 0x62 e os helpers de relay direcionado.
# Saida: C:\temp\world_slot_udp_relay.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


ADDRESSES = (0x0041C2B0, 0x0040B7D0, 0x00406930, 0x0041B8A0)
OUTPUT = r"C:\temp\world_slot_udp_relay.txt"

functions = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for address in ADDRESSES:
        function = functions.getFunctionAt(toAddr(address))
        if function is None:
            raise ValueError("funcao ausente em 0x%08X" % address)
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 180, monitor)
        body = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write(body.encode("ascii", "replace").decode("ascii"))
        output.write("\n--- assembly ---\n")
        for instruction in listing.getInstructions(function.getBody(), True):
            output.write("%s %s\n" % (instruction.getAddress(), instruction))

print("relay 0x62 extraido: %d funcoes" % len(ADDRESSES))
