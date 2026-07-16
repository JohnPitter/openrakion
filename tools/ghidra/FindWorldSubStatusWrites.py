# -*- coding: utf-8 -*-
# Localiza leituras/escritas de user+0x146C para separar membro, master e GM.
# Saida: C:\temp\world_substatus_refs.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

OFFSET = 0x146c
listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = {}
hits = []

for instruction in listing.getInstructions(True):
    found = False
    for operand in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(operand):
            if isinstance(obj, Scalar) and (obj.getUnsignedValue() & 0xffffffff) == OFFSET:
                found = True
    if not found:
        continue
    function = functions.getFunctionContaining(instruction.getAddress())
    hits.append((instruction.getAddress(), str(instruction)))
    if function:
        matches[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\world_substatus_refs.txt", "w") as output:
    output.write("=== instrucoes ===\n")
    for address, instruction in hits:
        output.write("%s %s\n" % (address, instruction))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(matches):
        function = matches[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("substatus: %d instrucoes, %d funcoes" % (len(hits), len(matches)))
