# -*- coding: utf-8 -*-
# Decompila os 88 consumidores da fila IScavengerWorldNet S->C.
# Saida: C:\temp\client_world_response_handlers.txt
# @category Rakion
import re

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


DISPATCHER = 0x36197320
OUTPUT = r"C:\temp\client_world_response_handlers.txt"

functions = currentProgram.getFunctionManager()
dispatcher = functions.getFunctionAt(toAddr(DISPATCHER))
if dispatcher is None:
    raise ValueError("dispatcher ausente")

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
result = decompiler.decompileFunction(dispatcher, 240, monitor)
if not result or not result.getDecompiledFunction():
    raise ValueError("falha ao decompilar dispatcher")

code = result.getDecompiledFunction().getC()
handlers = {}
current = None
for line in code.splitlines():
    match = re.search(r"case\s+(0x[0-9a-fA-F]+|[0-9]+):", line)
    if match:
        current = int(match.group(1), 0)
        continue
    if current is None or current in handlers:
        continue
    match = re.search(r"FUN_([0-9a-fA-F]{8})\s*\(", line)
    if match:
        handlers[current] = int(match.group(1), 16)

# O case 0 chama diretamente o callback virtual e nao possui consumidor separado.
if len(handlers) != 87:
    raise ValueError("esperados 87 handlers concretos, obtidos %d" % len(handlers))

with open(OUTPUT, "w") as output:
    output.write("dispatcher=0x%08X cases=88 concrete_handlers=%d\n" %
                 (DISPATCHER, len(handlers)))
    for opcode in sorted(handlers):
        address = handlers[opcode]
        function = functions.getFunctionAt(toAddr(address))
        if function is None:
            raise ValueError("handler 0x%02X ausente em 0x%08X" % (opcode, address))
        result = decompiler.decompileFunction(function, 180, monitor)
        body = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== opcode=0x%02X handler=0x%08X %s =====\n" %
                     (opcode, address, function.getName(True)))
        output.write(body.encode("ascii", "replace").decode("ascii"))

print("consumidores IScavengerWorldNet S->C: %d" % len(handlers))
