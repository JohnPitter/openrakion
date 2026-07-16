# -*- coding: utf-8 -*-
# Extrai a regra x87 e as constantes de preço de venda do World original.
# Saída: C:\temp\world_shop_pricing.txt
# @category Rakion
from java.lang import Double

TARGET = 0x0040A810
CONSTANTS = (0x00447670, 0x00447678)
listing = currentProgram.getListing()
memory = currentProgram.getMemory()
function = currentProgram.getFunctionManager().getFunctionContaining(toAddr(TARGET))

with open(r"C:\temp\world_shop_pricing.txt", "w") as output:
    output.write("=== constantes double ===\n")
    for address in CONSTANTS:
        bits = memory.getLong(toAddr(address))
        output.write("%08X bits=%016X value=%.17g\n" %
                     (address, bits & 0xffffffffffffffff, Double.longBitsToDouble(bits)))

    output.write("\n=== disassembly %s @ %s ===\n" %
                 (function.getName(True), function.getEntryPoint()))
    instructions = listing.getInstructions(function.getBody(), True)
    while instructions.hasNext():
        instruction = instructions.next()
        output.write("%s  %s\n" % (instruction.getAddress(), instruction.toString()))

print("world shop pricing: constantes e %s" % function.getName(True))
