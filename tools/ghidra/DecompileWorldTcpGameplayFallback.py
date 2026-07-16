# -*- coding: utf-8 -*-
# Extrai tunneling e ping TCP do gameplay no worldserv.exe v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x00405620,
    0x004056D0,
    0x004057B0,
    0x00405860,
    0x00405F30,
    0x004060A0,
    0x004062C0,
    0x00425620,
    0x004256D0,
    0x004257B0,
    0x00425860,
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_tcp_gameplay_fallback.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("fallback TCP de gameplay extraido")
