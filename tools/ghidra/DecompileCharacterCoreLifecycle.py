# -*- coding: utf-8 -*-
# Extrai o ciclo principal de personagem 0x12/0x13/0x15/0x1A.
# Saidas: C:\temp\world_character_core_lifecycle.txt,
#         C:\temp\engine_character_core_lifecycle.txt e
#         C:\temp\rakion_character_core_lifecycle.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x0041FCD0, "handler C->S 0x12 create"),
        (0x0041FE10, "handler C->S 0x13 delete"),
        (0x00420120, "handler C->S 0x15 buddy name"),
        (0x00420840, "handler C->S 0x1A tutorial clear"),
        (0x0041B940, "enfileira comando DB"),
        (0x00412280, "worker DB interno 0x07 create"),
        (0x00412530, "worker DB interno 0x08 delete"),
        (0x0041C3D0, "callback DB de create"),
        (0x00427570, "callback DB de delete"),
        (0x0040AB00, "projeta snapshot do personagem no delete"),
        (0x0041CB60, "callback DB de buddy name"),
    ),
    "engine.dll": (
        (0x36190D20, "builder C->S 0x12 create"),
        (0x36190DB0, "builder C->S 0x13 delete"),
        (0x36190E70, "builder C->S 0x15 buddy name"),
        (0x36191090, "builder C->S 0x1A tutorial clear"),
        (0x36192E30, "parser S->C 0x12 create"),
        (0x36192E70, "parser S->C 0x13 delete"),
        (0x36192FB0, "parser S->C 0x15 buddy name"),
    ),
    "rakion.bin": (
        (0x0047C4D0, "callback UI S->C 0x12 create"),
        (0x0047C7A0, "callback UI S->C 0x13 delete"),
        (0x004785B0, "callback UI S->C 0x15 buddy name"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_character_core_lifecycle.txt",
    "engine.dll": r"C:\temp\engine_character_core_lifecycle.txt",
    "rakion.bin": r"C:\temp\rakion_character_core_lifecycle.txt",
}


def decompile(function, decompiler, monitor):
    result = decompiler.decompileFunction(function, 300, monitor)
    if result and result.decompileCompleted():
        return result.getDecompiledFunction().getC()
    return "(falha na decompilacao)"


def ascii_text(value):
    return value.encode("ascii", "replace").decode("ascii")


program_name = currentProgram.getName().lower()
if program_name not in TARGETS:
    raise RuntimeError("programa nao suportado: %s" % program_name)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
output_path = OUTPUTS[program_name]

with open(output_path, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    for address, purpose in TARGETS[program_name]:
        function = manager.getFunctionAt(toAddr(address))
        output.write("\n===== %s @ %08x - %s =====\n" % (
            function.getName() if function else "funcao ausente",
            address,
            purpose,
        ))
        if function:
            output.write(ascii_text(decompile(function, decompiler, monitor)))

print("Character core lifecycle extraido: %s" % output_path)
