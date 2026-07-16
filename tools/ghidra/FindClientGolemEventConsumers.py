# -*- coding: utf-8 -*-
# Procura operandos crus de eventos Golem/Golden Sword no dump runtime de entitiesmp.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
from jarray import array


TARGETS = (
    ("EGoldSword_vtable", 0x352D07AC),
    ("EMasterGolemDamage_vtable", 0x352D082C),
    ("EGoldGolemRespawn_vtable", 0x352D328C),
    ("EGoldGolemRebirth_vtable", 0x352D329C),
    ("EMasterGolemRespawn_vtable", 0x352D6DF4),
    ("EGoldSword_id", 0x044D000B),
    ("EMasterGolemDamage_id", 0x044D0015),
    ("EGoldGolemRespawn_id", 0x04690000),
    ("EGoldGolemRebirth_id", 0x04690001),
    ("EMasterGolemRespawn_id", 0x04650000),
)
OUTPUT = r"C:\temp\client_golem_event_consumers.txt"

memory = currentProgram.getMemory()
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
matches = []

for name, value in TARGETS:
    raw = [(value >> shift) & 0xff for shift in (0, 8, 16, 24)]
    pattern = array([byte - 256 if byte > 127 else byte for byte in raw], "b")
    cursor = memory.getMinAddress()
    maximum = memory.getMaxAddress()
    while cursor <= maximum:
        found = memory.findBytes(cursor, maximum, pattern, None, True, monitor)
        if found is None:
            break
        function = manager.getFunctionContaining(found)
        matches.append(
            "%s 0x%08x @ %s em %s @ %s\n" % (
                name,
                value,
                found,
                function.getName() if function else "(sem funcao)",
                function.getEntryPoint() if function else "-",
            )
        )
        if function is not None:
            functions[str(function.getEntryPoint())] = function
        cursor = found.add(1)

with open(OUTPUT, "w") as output:
    output.write("===== ocorrencias =====\n")
    for match in matches:
        output.write(match)
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("consumidores de eventos Golem extraidos em " + OUTPUT)
