# -*- coding: utf-8 -*-
# Extrai objetivos Golem/Boss e a decisão de round do worldserv.exe v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x00405D70,
    0x00405EF0,
    0x004079D0,
    0x00407BE0,
    0x00408440,
    0x004087D0,
    0x00409940,
    0x00424980,
    0x00425CC0,
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_golem_boss.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("objetivos Golem/Boss extraidos")
