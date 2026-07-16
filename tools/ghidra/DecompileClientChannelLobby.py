# -*- coding: utf-8 -*-
# Extrai o dispatcher e os consumidores S->C de canal/lobby da engine.dll v258.
# Saída: C:\temp\client_channel_lobby.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x36197320,  # dispatcher World S->C
    0x361931E0,  # 0x1D lista de canais
    0x361932A0,  # 0x1E membros
    0x361933F0,  # 0x1F entrada
    0x36193490,  # 0x20 saída
    0x361934B0,  # 0x21 criação
    0x361934E0,  # 0x22 chat
    0x36193550,  # 0x25 nome
    0x36193590,  # 0x26 senha
    0x361935D0,  # 0x27 capacidade
    0x361935F0,  # 0x28 owner
    0x36193610,  # 0x29 ping request
    0x36193630,  # 0x2A ping response
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\client_channel_lobby.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== função ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("canal/lobby do cliente extraído")
