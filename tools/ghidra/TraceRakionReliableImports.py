# -*- coding: utf-8 -*-
# Localiza producers reliable no rakion.bin, incluindo chamadas via import do engine.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
import jarray


OUTPUT = r"C:\temp\rakion_reliable_imports.txt"
TYPES = (0x0313, 0x8313)
TARGET_FUNCTIONS = (0x0045C5F0, 0x0045C6F0)
DECOMPILE_FUNCTIONS = (0x0045CC50, 0x0045CE60, 0x0045C560)

memory = currentProgram.getMemory()
references = currentProgram.getReferenceManager()
manager = currentProgram.getFunctionManager()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
callers = {}
lines = []


def remember(reference):
    caller = manager.getFunctionContaining(reference.getFromAddress())
    lines.append("%s <- %s @ %s\n" % (
        reference.getFromAddress(),
        caller.getName() if caller else "(sem funcao)",
        caller.getEntryPoint() if caller else "-"))
    if caller:
        callers[str(caller.getEntryPoint())] = caller


for function in manager.getFunctions(True):
    name = function.getName()
    if "Reliable" not in name and "OtherClient" not in name:
        continue
    refs = list(references.getReferencesTo(function.getEntryPoint()))
    if not refs:
        continue
    lines.append("===== IMPORT/FUNCTION %s @ %s =====\n" % (name, function.getEntryPoint()))
    for reference in refs:
        remember(reference)

seen_symbols = set()
for symbol in symbols.getAllSymbols(True):
    name = symbol.getName()
    if "Reliable" not in name and "OtherClient" not in name:
        continue
    address = symbol.getAddress()
    key = "%s@%s" % (name, address)
    if key in seen_symbols:
        continue
    seen_symbols.add(key)
    refs = list(references.getReferencesTo(address))
    if not refs:
        continue
    lines.append("===== SYMBOL %s @ %s =====\n" % (name, address))
    for reference in refs:
        remember(reference)

for message_type in TYPES:
    values = [message_type & 0xff, (message_type >> 8) & 0xff]
    pattern = jarray.array([value if value < 0x80 else value - 0x100 for value in values], "b")
    lines.append("===== LITERAL 0x%04x =====\n" % message_type)
    for block in memory.getBlocks():
        if not block.isInitialized():
            continue
        cursor = block.getStart()
        while cursor and cursor.compareTo(block.getEnd()) <= 0:
            hit = memory.findBytes(cursor, block.getEnd(), pattern, None, True, monitor)
            if hit is None:
                break
            for reference in references.getReferencesTo(hit):
                remember(reference)
            cursor = hit.add(1)

for target_address in TARGET_FUNCTIONS:
    target = manager.getFunctionAt(toAddr(target_address))
    lines.append("===== PRODUCER %s @ %s =====\n" % (
        target.getName() if target else "(sem funcao)", toAddr(target_address)))
    for reference in references.getReferencesTo(toAddr(target_address)):
        remember(reference)

for address in DECOMPILE_FUNCTIONS:
    function = manager.getFunctionContaining(toAddr(address))
    if function:
        callers[str(function.getEntryPoint())] = function

with open(OUTPUT, "w") as output:
    output.writelines(lines)
    for key in sorted(callers):
        function = callers[key]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("imports reliable do rakion.bin extraidos")
