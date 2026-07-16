# -*- coding: utf-8 -*-
# Extrai os handlers de lista, entrada, busca rapida e criacao de sala do World v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.symbol import RefType
from ghidra.util.task import ConsoleTaskMonitor

ROOTS = (0x00422C90, 0x00423100, 0x00423300, 0x00423580)
OUTPUT = r"C:\temp\world_room_entry.txt"

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()


def direct_callees(function):
    result = []
    listing = currentProgram.getListing()
    instruction = listing.getInstructionAt(function.getEntryPoint())
    while instruction is not None and function.getBody().contains(instruction.getAddress()):
        for reference in instruction.getReferencesFrom():
            if reference.getReferenceType() == RefType.UNCONDITIONAL_CALL:
                callee = manager.getFunctionAt(reference.getToAddress())
                if callee is not None and callee not in result:
                    result.append(callee)
        instruction = instruction.getNext()
    return result


functions = []
for address in ROOTS:
    root = manager.getFunctionAt(toAddr(address))
    if root is None:
        continue
    if root not in functions:
        functions.append(root)
    for callee in direct_callees(root):
        if callee not in functions:
            functions.append(callee)

with open(OUTPUT, "w") as output:
    for function in functions:
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("fluxo de entrada em sala extraido para " + OUTPUT)
