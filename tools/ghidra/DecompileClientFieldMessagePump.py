# -*- coding: utf-8 -*-
# Extrai o pump CNet e o dispatcher externo de mensagens de partida do rakion.bin.
# Saida: C:\temp\client_field_message_pump.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x004124A0,  # loop CNet::RecvData
    0x00411760,  # dispatcher externo do rakion.bin
)
OUTPUT = r"C:\temp\client_field_message_pump.txt"

functions = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for address in TARGETS:
        function = functions.getFunctionAt(toAddr(address))
        if function is None:
            raise ValueError("funcao ausente em 0x%08X" % address)
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 300, monitor)
        body = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else None)
        if body is None:
            raise ValueError("falha ao decompilar 0x%08X" % address)
        output.write(body.encode("ascii", "replace").decode("ascii"))
        output.write("\n--- assembly ---\n")
        for instruction in listing.getInstructions(function.getBody(), True):
            output.write("%s %s\n" % (instruction.getAddress(), instruction))

print("pump CNet/P2P extraido: RecvData + dispatcher externo")
