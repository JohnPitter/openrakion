# Localiza textos ASCII e lista referencias reconhecidas pelo projeto.
# @category Rakion
import jarray
from ghidra.util.task import ConsoleTaskMonitor

memory = currentProgram.getMemory()
references = currentProgram.getReferenceManager()
functions = currentProgram.getFunctionManager()
monitor = ConsoleTaskMonitor()

print("PROGRAM=%s" % currentProgram.getName())
for token in getScriptArgs():
    pattern = jarray.array([ord(char) for char in token], "b")
    print("\nTOKEN=%s" % token)
    for block in memory.getBlocks():
        if not block.isInitialized():
            continue
        cursor = block.getStart()
        while cursor is not None and cursor.compareTo(block.getEnd()) <= 0:
            hit = memory.findBytes(cursor, block.getEnd(), pattern, None, True, monitor)
            if hit is None:
                break
            refs = list(references.getReferencesTo(hit))
            print("  HIT=%s REFS=%d" % (hit, len(refs)))
            for reference in refs:
                function = functions.getFunctionContaining(reference.getFromAddress())
                print("    %s | %s" % (
                    reference.getFromAddress(),
                    function.getName() if function else "(sem funcao)",
                ))
            cursor = hit.add(1)

print("##### DONE #####")
