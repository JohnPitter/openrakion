# -*- coding: utf-8 -*-
# Rastreia a aplicação visual/de stats após o move 0x31 no cliente.
# Saída: C:\temp\client_equipment_effects.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = (0x0047D1D0, 0x00477E70, 0x0047AB30, 0x0044E530, 0x00462C40)
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for target_address in TARGETS:
    target = manager.getFunctionContaining(toAddr(target_address))
    if not target:
        continue
    functions[target.getEntryPoint().getOffset()] = target
    for address in target.getBody().getAddresses(True):
        for reference in references.getReferencesFrom(address):
            called = manager.getFunctionAt(reference.getToAddress())
            if called and not called.isExternal():
                functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\client_equipment_effects.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client equipment effects: %d funcoes" % len(functions))
