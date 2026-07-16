# -*- coding: utf-8 -*-
# Localiza referencias aos opcodes 0x5D/0x5E/0x5F no modulo cliente aberto.
# Saida: C:\temp\client_field_vote_consumers.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

OPCODES = set([0x5d, 0x5e, 0x5f])
listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = {}
hits = []

for instruction in listing.getInstructions(True):
    values = []
    for operand in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(operand):
            if isinstance(obj, Scalar):
                value = obj.getUnsignedValue() & 0xffffffff
                if value in OPCODES:
                    values.append(value)
    if not values:
        continue
    function = functions.getFunctionContaining(instruction.getAddress())
    hits.append((instruction.getAddress(), str(instruction), values))
    if function:
        matches[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\client_field_vote_consumers.txt", "w") as output:
    output.write("program=%s\n=== instrucoes ===\n" % currentProgram.getName())
    for address, instruction, values in hits:
        output.write("%s %s opcodes=%s\n" % (address, instruction, values))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(matches):
        function = matches[entry]
        result = decompiler.decompileFunction(function, 120, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("consumidores de voto: %d instrucoes, %d funcoes" % (len(hits), len(matches)))
