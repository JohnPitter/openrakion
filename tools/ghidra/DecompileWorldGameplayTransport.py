# -*- coding: utf-8 -*-
# Extrai o transporte UDP realmente despachado pelo worldserv.exe v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x00404010,  # sendto UDP
    0x004040D0,  # recvfrom UDP
    0x0040AB90,  # grava endereco/portas anunciada e observada
    0x0040ABE0,  # le endereco/porta primarios da sessao
    0x00425D80,  # handshake UDP port1
    0x00425FA0,  # handshake UDP port2
    0x00429530,  # dispatcher UDP: somente 0x0201/0202/0401/0402
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_gameplay_transport.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== funcao ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("transporte de gameplay extraido")
