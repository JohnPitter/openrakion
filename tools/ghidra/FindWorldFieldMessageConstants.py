# -*- coding: utf-8 -*-
# Localiza constantes dos frames de ciclo de field no codigo do World v258.
# @category Rakion
from ghidra.program.model.scalar import Scalar

TARGETS = (0x45, 0x46, 0x48, 0x49, 0x4A, 0x4F)
OUTPUT = r"C:\temp\world_field_message_constants.txt"
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()

with open(OUTPUT, "w") as output:
    instruction = listing.getInstructions(True)
    while instruction.hasNext():
        item = instruction.next()
        address = item.getAddress().getOffset()
        if address < 0x00400000 or address >= 0x00430000:
            continue
        values = set()
        for index in range(item.getNumOperands()):
            for obj in item.getOpObjects(index):
                if isinstance(obj, Scalar):
                    value = obj.getUnsignedValue()
                    if value in TARGETS:
                        values.add(value)
        if not values:
            continue
        function = manager.getFunctionContaining(item.getAddress())
        name = function.getName() if function is not None else "-"
        output.write("%s %s constants=%s function=%s\n" %
                     (item.getAddress(), item, ",".join("%02X" % value for value in values), name))

print("constantes de mensagens de field extraidas para " + OUTPUT)
