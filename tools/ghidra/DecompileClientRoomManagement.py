# -*- coding: utf-8 -*-
# Extrai o dispatcher e consumidores S->C de lista, roster e gerenciamento de sala.
# Saída: C:\temp\client_room_management.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x36197320,  # dispatcher World S->C
    0x36193900,  # 0x36 lista
    0x36196CE0,  # 0x37 snapshot/roster
    0x361970E0,  # 0x38 entrada/resultado
    0x36193A60,  # 0x39 fim de quick join
    0x36193A70,  # 0x3A saída
    0x36193A90,  # 0x3B criação
    0x36193AC0,  # 0x3C troca de master
    0x36193AE0,  # 0x3D ready
    0x36193B10,  # 0x3E troca de time
    0x36193B50,  # 0x41 regra
    0x36193C50,  # 0x42 slot
    0x36193C80,  # 0x43 start
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\client_room_management.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== função ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("gerenciamento de salas do cliente extraído")
