# -*- coding: utf-8 -*-
# Extrai a maquina de estado de partida do worldserv.exe v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x004065E0,  # inicializacao do round
    0x004079D0,  # engage/start do field
    0x00407BE0,  # fim do match e frame 0x44
    0x00408440,  # player ready e frame 0x48
    0x00409940,  # motor Pre/Playing/RoundEnd
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_match_lifecycle.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== funcao ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("ciclo de partida extraido")
