# -*- coding: utf-8 -*-
# Exporta o assembly do construtor da resposta 0x0c para fixar offsets do header.
# @category Rakion
function = currentProgram.getFunctionManager().getFunctionAt(toAddr(0x00426B30))
listing = currentProgram.getListing()

with open(r"C:\temp\world_login_response_asm.txt", "w") as output:
    for instruction in listing.getInstructions(function.getBody(), True):
        output.write("%s  %s\n" % (instruction.getAddress(), instruction))

print("assembly da resposta de login extraido")
