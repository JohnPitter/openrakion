# -*- coding: utf-8 -*-
# Rastreia produtores/consumidores da fila drenada por ProcessWorldRecvBuffer.
# Saida: C:\temp\client_world_receive_queue.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


GLOBALS = (0x3635F1F0, 0x3635F1F4)
OUTPUT = r"C:\temp\client_world_receive_queue.txt"

references = currentProgram.getReferenceManager()
functions = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
owners = {}

with open(OUTPUT, "w") as output:
    for address in GLOBALS:
        refs = list(references.getReferencesTo(toAddr(address)))
        output.write("GLOBAL=0x%08X REFS=%d\n" % (address, len(refs)))
        for reference in refs:
            owner = functions.getFunctionContaining(reference.getFromAddress())
            instruction = listing.getInstructionAt(reference.getFromAddress())
            output.write("  %s owner=%s instruction=%s\n" % (
                reference.getFromAddress(),
                owner.getName(True) if owner else "-",
                instruction if instruction else "-",
            ))
            if owner is not None:
                owners[owner.getEntryPoint().getOffset()] = owner
    for address in sorted(owners):
        owner = owners[address]
        output.write("\n===== %s @ %s =====\n" %
                     (owner.getName(True), owner.getEntryPoint()))
        result = decompiler.decompileFunction(owner, 300, monitor)
        if not result or not result.getDecompiledFunction():
            raise ValueError("falha ao decompilar 0x%08X" % address)
        output.write(result.getDecompiledFunction().getC())
        output.write("\n")

print("fila World recv: %d funcoes em %s" % (len(owners), OUTPUT))
