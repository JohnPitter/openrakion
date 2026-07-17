# -*- coding: utf-8 -*-
# Audita consumidores dos timestamps e durações comerciais de Steam/Scouter.
# @category Rakion


TARGETS = (
    0x2C4C,
    0x2C50,
    0x2C64,
    0x2C68,
    30000,
    60000,
)

listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
hits = {target: [] for target in TARGETS}

instructions = listing.getInstructions(True)
while instructions.hasNext():
    instruction = instructions.next()
    for operand_index in range(instruction.getNumOperands()):
        values = {
            obj.getValue()
            for obj in instruction.getOpObjects(operand_index)
            if hasattr(obj, "getValue")
        }
        for target in TARGETS:
            if target not in values:
                continue
            function = manager.getFunctionContaining(instruction.getAddress())
            owner = function.getName() if function is not None else "<sem funcao>"
            hits[target].append((instruction.getAddress(), owner, instruction))

output_path = r"C:\temp\potion_duration_consumers_%s.txt" % currentProgram.getName()
with open(output_path, "w") as output:
    output.write("program=%s\n" % currentProgram.getName())
    for target in TARGETS:
        output.write("\ntarget=0x%x hits=%d\n" % (target, len(hits[target])))
        for address, owner, instruction in hits[target]:
            output.write("%s %s :: %s\n" % (address, owner, instruction))

print("auditoria de duracoes extraida em %s" % output_path)
