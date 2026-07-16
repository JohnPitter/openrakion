# -*- coding: utf-8 -*-
# Extrai os serializers do roster de sala no worldserv.exe v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (0x00406F40, 0x0040B7F0)
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_roster_serializers.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== funcao ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("serializers de roster extraidos")
