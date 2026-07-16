# -*- coding: utf-8 -*-
# Extrai o dispatcher S->C que chama os consumidores de voto/convite.
# Saidas: C:\temp\client_world_message_dispatcher.txt/.asm.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

function = currentProgram.getFunctionManager().getFunctionAt(toAddr(0x36197320))
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
result = decompiler.decompileFunction(function, 180, ConsoleTaskMonitor())
code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"

with open(r"C:\temp\client_world_message_dispatcher.txt", "w") as output:
    output.write(code.encode("ascii", "replace").decode("ascii"))

with open(r"C:\temp\client_world_message_dispatcher.asm.txt", "w") as output:
    for instruction in currentProgram.getListing().getInstructions(function.getBody(), True):
        output.write("%s %s\n" % (instruction.getAddress(), instruction))

print("dispatcher S->C do cliente extraido")
