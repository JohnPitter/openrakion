# -*- coding: utf-8 -*-
# Resolve a vtable do callback de move 0x31 e seus vizinhos de compra/venda.
# Saída: C:\temp\rakion_inventory_callback_vtable.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGET = 0x0047D1D0
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
memory = currentProgram.getMemory()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
targets = {}
rows = []

for reference in references.getReferencesTo(toAddr(TARGET)):
    source = reference.getFromAddress()
    for delta in range(-0x10, 0x14, 4):
        slot = source.add(delta)
        try:
            pointer = memory.getInt(slot) & 0xffffffff
        except:
            continue
        function = manager.getFunctionContaining(toAddr(pointer))
        if not function and 0x00400000 <= pointer < 0x00500000:
            try:
                disassemble(toAddr(pointer))
                function = createFunction(toAddr(pointer), None)
            except:
                function = None
        rows.append((source, delta, slot, pointer, function))
        if function:
            targets[pointer] = function

with open(r"C:\temp\rakion_inventory_callback_vtable.txt", "w") as output:
    output.write("=== slots ao redor de FUN_0047D1D0 ===\n")
    for source, delta, slot, pointer, function in rows:
        name = function.getName(True) if function else "-"
        output.write("ref=%s delta=%+d slot=%s ptr=%08X function=%s\n" %
                     (source, delta, slot, pointer, name))
    for pointer in sorted(targets):
        function = targets[pointer]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("rakion inventory callback vtable: %d refs, %d funcoes" % (len(rows), len(targets)))
