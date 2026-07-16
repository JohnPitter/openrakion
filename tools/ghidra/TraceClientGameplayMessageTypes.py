# -*- coding: utf-8 -*-
# Localiza literais e referências dos tipos internos de gameplay do engine.dll v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
import jarray


TYPES = (0x030D, 0x0313, 0x0315, 0x0319, 0x830C, 0x8313, 0x8315)

memory = currentProgram.getMemory()
references = currentProgram.getReferenceManager()
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()


def hits_for(message_type):
    values = [message_type & 0xff, (message_type >> 8) & 0xff]
    pattern = jarray.array([value if value < 0x80 else value - 0x100 for value in values], "b")
    hits = []
    for block in memory.getBlocks():
        if not block.isInitialized():
            continue
        cursor = block.getStart()
        while cursor and cursor.compareTo(block.getEnd()) <= 0:
            hit = memory.findBytes(cursor, block.getEnd(), pattern, None, True, monitor)
            if hit is None:
                break
            refs = list(references.getReferencesTo(hit))
            if refs:
                hits.append((hit, refs))
            cursor = hit.add(1)
    return hits


functions = {}
lines = []
for message_type in TYPES:
    lines.append("===== 0x%04x =====\n" % message_type)
    for hit, refs in hits_for(message_type):
        for reference in refs:
            caller = manager.getFunctionContaining(reference.getFromAddress())
            lines.append("%s <- %s em %s @ %s\n" % (
                hit,
                reference.getFromAddress(),
                caller.getName() if caller else "(sem funcao)",
                caller.getEntryPoint() if caller else "-",
            ))
            if caller:
                functions[str(caller.getEntryPoint())] = caller

with open(r"C:\temp\client_gameplay_message_types.txt", "w") as output:
    for line in lines:
        output.write(line)
    for key in sorted(functions):
        function = functions[key]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("tipos internos de gameplay rastreados")
