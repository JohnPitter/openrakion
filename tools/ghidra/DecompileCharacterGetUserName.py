# -*- coding: utf-8 -*-
# Extrai o fluxo 0x19 CharacterGetUserName nas tres pontas.
# Saidas: C:\temp\world_character_get_user_name.txt,
#         C:\temp\engine_character_get_user_name.txt e
#         C:\temp\rakion_character_get_user_name.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x00420760, "handler C->S 0x19 CharacterGetUserName"),
        (0x0041B940, "envio pelo canal de mensagem"),
    ),
    "engine.dll": (
        (0x36191020, "builder C->S 0x19 CharacterGetUserName"),
        (0x36193170, "parser da resposta de CharacterGetUserName"),
    ),
    "rakion.bin": (
        (0x00476420, "ponte UI para SendCharacterGetUserName"),
        (0x00476450, "callback UI de CharacterGetUserName"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_character_get_user_name.txt",
    "engine.dll": r"C:\temp\engine_character_get_user_name.txt",
    "rakion.bin": r"C:\temp\rakion_character_get_user_name.txt",
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
        if function is None:
            disassemble(toAddr(address))
            function = createFunction(toAddr(address), None)
        output.write("\n===== %s @ %08x - %s =====\n" % (
            function.getName() if function else "funcao ausente",
            address,
            purpose,
        ))
        if function:
            output.write(ascii_text(decompile(function, decompiler, monitor)))

print("CharacterGetUserName extraido: %s" % output_path)
