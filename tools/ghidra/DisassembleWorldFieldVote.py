# -*- coding: utf-8 -*-
# Exporta o assembly das rotinas de votacao para fixar retornos em AL e efeitos.
# Saida: C:\temp\world_field_vote_asm.txt
# @category Rakion

ADDRESSES = (0x00425A70, 0x00425BB0, 0x0040A420, 0x004098E0,
             0x00409810, 0x004090B0, 0x004068C0)
functions = currentProgram.getFunctionManager()
listing = currentProgram.getListing()

with open(r"C:\temp\world_field_vote_asm.txt", "w") as output:
    for address in ADDRESSES:
        function = functions.getFunctionAt(toAddr(address))
        if not function:
            output.write("\n===== ausente @ %08X =====\n" % address)
            continue
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        for instruction in listing.getInstructions(function.getBody(), True):
            output.write("%s  %s\n" % (instruction.getAddress(), instruction))

print("assembly das rotinas de votacao extraido")
