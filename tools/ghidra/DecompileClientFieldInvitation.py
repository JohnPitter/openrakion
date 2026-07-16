# -*- coding: utf-8 -*-
# Extrai builder e consumidor S->C do convite 0x72.
# Saída: C:\temp\client_field_invitation.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (0x36191AF0, 0x36193F40)
fm = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for address in ADDRESSES:
    root = fm.getFunctionContaining(toAddr(address))
    functions[root.getEntryPoint().getOffset()] = root
    for called in root.getCalledFunctions(monitor):
        functions[called.getEntryPoint().getOffset()] = called

with open(r"C:\temp\client_field_invitation.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client FieldInvitation: %d funções" % len(functions))
