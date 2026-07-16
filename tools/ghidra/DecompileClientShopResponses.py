# -*- coding: utf-8 -*-
# Decompila os consumidores S->C de compra 0x2E e venda 0x2F no engine.
# Saída: C:\temp\client_shop_responses.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = (0x36192F90, 0x36192FB0, 0x36195640, 0x361936A0)
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
            if called:
                functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\client_shop_responses.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("client shop responses: %d funcoes" % len(functions))
