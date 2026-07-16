# -*- coding: utf-8 -*-
# Cataloga todos os cases do dispatcher IScavengerWorldNet S->C de engine.dll.
# Saida: C:\temp\client_world_response_catalog.tsv
# @category Rakion
import re

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


DISPATCHER = 0x36197320
OUTPUT = r"C:\temp\client_world_response_catalog.tsv"

manager = currentProgram.getFunctionManager()
function = manager.getFunctionAt(toAddr(DISPATCHER))
if function is None:
    raise ValueError("dispatcher ausente em 0x%08X" % DISPATCHER)

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
result = decompiler.decompileFunction(function, 240, ConsoleTaskMonitor())
if not result or not result.getDecompiledFunction():
    raise ValueError("falha ao decompilar dispatcher")

code = result.getDecompiledFunction().getC()
cases = {}
current = None
for line in code.splitlines():
    match = re.search(r"case\s+(0x[0-9a-fA-F]+|[0-9]+):", line)
    if match:
        current = int(match.group(1), 0)
        cases.setdefault(current, "-")
        continue
    if current is None or cases[current] != "-":
        continue
    match = re.search(r"FUN_([0-9a-fA-F]{8})\s*\(", line)
    if match:
        cases[current] = "0x" + match.group(1).upper()

cases[0] = "virtual+0x15C"
callbacks = {0: "callback+0x15C"}

for opcode, handler in cases.items():
    if opcode == 0:
        continue
    address = int(handler, 16)
    function = manager.getFunctionAt(toAddr(address))
    if function is None:
        raise ValueError("handler ausente para 0x%02X" % opcode)
    result = decompiler.decompileFunction(function, 180, ConsoleTaskMonitor())
    if not result or not result.getDecompiledFunction():
        raise ValueError("falha ao decompilar handler de 0x%02X" % opcode)
    body = result.getDecompiledFunction().getC()
    match = re.search(
        r"\(\*\*\(code \*\*\)\(.{0,160}?\+\s*(0x[0-9a-fA-F]+|[0-9]+)\)\)\s*\(",
        body,
        re.DOTALL,
    )
    if match:
        callbacks[opcode] = "callback+0x%X" % int(match.group(1), 0)
    elif opcode == 0x61 and "local_1004 = 0x61" in body and "FUN_361905e0" in body:
        callbacks[opcode] = "envia-0x61"
    else:
        raise ValueError("destino final nao resolvido para 0x%02X" % opcode)

with open(OUTPUT, "w") as output:
    output.write("opcode\thandler\tdestino\n")
    for opcode in sorted(cases):
        output.write("0x%02X\t%s\t%s\n" %
                     (opcode, cases[opcode], callbacks[opcode]))

print("catalogo IScavengerWorldNet S->C: %d cases em %s" % (len(cases), OUTPUT))
