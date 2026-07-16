# -*- coding: utf-8 -*-
# Cataloga os cases do dispatcher CNet/P2P em engine.dll.
# Saida: C:\temp\client_field_message_catalog.tsv
# @category Rakion
import re

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


DISPATCHER = 0x3610D7C0
OUTPUT = r"C:\temp\client_field_message_catalog.tsv"

manager = currentProgram.getFunctionManager()
function = manager.getFunctionAt(toAddr(DISPATCHER))
if function is None:
    raise ValueError("CSessionState::HandleMessage ausente")

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
result = decompiler.decompileFunction(function, 300, ConsoleTaskMonitor())
if not result or not result.getDecompiledFunction():
    raise ValueError("falha ao decompilar CSessionState::HandleMessage")

code = result.getDecompiledFunction().getC()
cases = []
for match in re.finditer(r"case\s+(0x[0-9a-fA-F]+|[0-9]+):", code):
    value = int(match.group(1), 0)
    if value not in cases:
        cases.append(value)

expected = [0x307, 0x308, 0x309, 0x30A, 0x30B, 0x30C, 0x30F, 0x310, 0x312]
if sorted(cases) != expected:
    raise ValueError("cases inesperados: %s" % [hex(value) for value in cases])

with open(OUTPUT, "w") as output:
    output.write("logical_type\tdispatcher\n")
    for value in sorted(cases):
        output.write("0x%04X\t0x%08X\n" % (value, DISPATCHER))

print("catalogo CNet/P2P engine: %d cases em %s" % (len(cases), OUTPUT))
