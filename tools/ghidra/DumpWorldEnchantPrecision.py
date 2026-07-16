# -*- coding: utf-8 -*-
# Extrai constantes IEEE-754 e instrucoes x87 da regra de refino do World original.
# Saida: C:\temp\world_enchant_precision.txt
# @category Rakion
from java.lang import Float

TARGET = 0x0040C310
CONSTANTS = range(0x004477C0, 0x00447808, 4)
listing = currentProgram.getListing()
memory = currentProgram.getMemory()
function = currentProgram.getFunctionManager().getFunctionContaining(toAddr(TARGET))

with open(r"C:\temp\world_enchant_precision.txt", "w") as output:
    output.write("=== constantes float ===\n")
    for address in CONSTANTS:
        bits = memory.getInt(toAddr(address))
        unsigned = bits & 0xffffffff
        output.write("%08X bits=%08X value=%.17g\n" %
                     (address, unsigned, Float.intBitsToFloat(bits)))

    output.write("\n=== disassembly %s @ %s ===\n" %
                 (function.getName(True), function.getEntryPoint()))
    instructions = listing.getInstructions(function.getBody(), True)
    while instructions.hasNext():
        instruction = instructions.next()
        output.write("%s  %s\n" % (instruction.getAddress(), instruction.toString()))

print("world enchant precision: constantes e %s" % function.getName(True))
