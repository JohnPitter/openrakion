# -*- coding: utf-8 -*-
# Extrai contratos, persistencia e consumo de CharacterStateClear/ChangeCharName.
# Saidas: C:\temp\world_character_reset_rename.txt,
#         C:\temp\engine_character_reset_rename.txt e
#         C:\temp\rakion_character_reset_rename.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x004208E0, "handler C->S 0x1B"),
        (0x00420A40, "handler C->S 0x1C"),
        (0x0040BD80, "valida pagamento e cupom"),
        (0x0041B940, "enfileira comando DB"),
        (0x00413CD0, "worker DB interno 0x10 state clear"),
        (0x004144F0, "worker DB interno 0x11 rename"),
        (0x00427760, "callback DB de state clear"),
        (0x004278D0, "callback DB de rename"),
        (0x0041D570, "seleciona faixa de presente"),
        (0x0041D650, "publica presente na sessao"),
    ),
    "engine.dll": (
        (0x361910D0, "builder C->S 0x1B"),
        (0x36191140, "builder C->S 0x1C"),
        (0x36195400, "parser S->C 0x1B"),
        (0x36195500, "parser S->C 0x1C"),
    ),
    "rakion.bin": (
        (0x0047DFF0, "callback UI S->C 0x1B"),
        (0x004787B0, "callback UI S->C 0x1C"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_character_reset_rename.txt",
    "engine.dll": r"C:\temp\engine_character_reset_rename.txt",
    "rakion.bin": r"C:\temp\rakion_character_reset_rename.txt",
}


def decompile(function, decompiler, monitor):
    result = decompiler.decompileFunction(function, 240, monitor)
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

print("Character reset/rename extraido: %s" % output_path)
