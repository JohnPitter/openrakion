# -*- coding: utf-8 -*-
# Localiza leitores/escritores dos dez slots de penalidade temporaria da votacao.
# Saida: C:\temp\world_field_vote_penalty.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

OFFSETS = set([0x358, 0x35c, 0x1b7740])
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
                if value in OFFSETS:
                    values.append(value)
    if not values:
        continue
    function = functions.getFunctionContaining(instruction.getAddress())
    hits.append((instruction.getAddress(), str(instruction), values))
    if function:
        matches[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\world_field_vote_penalty.txt", "w") as output:
    output.write("=== instrucoes ===\n")
    for address, instruction, values in hits:
        output.write("%s %s offsets=%s\n" % (address, instruction, values))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(matches):
        function = matches[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("penalidade de voto: %d instrucoes, %d funcoes" % (len(hits), len(matches)))
