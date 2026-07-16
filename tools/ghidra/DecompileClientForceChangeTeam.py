# -*- coding: utf-8 -*-
# Extrai builder 0x5B e call sites diretos no engine.dll/rakion.bin.
# Saída definida por OUT_PATH para permitir executar nos dois projetos.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
import os

ADDRESS = 0x36192970
OUT_PATH = os.environ.get("OUT_PATH", r"C:\temp\client_force_change_team.txt")
fm = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

root = fm.getFunctionAt(toAddr(ADDRESS))
if root:
    functions[root.getEntryPoint().getOffset()] = root
    for caller in root.getCallingFunctions(monitor):
        functions[caller.getEntryPoint().getOffset()] = caller

with open(OUT_PATH, "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Client ForceChangeTeam: %d funções" % len(functions))
