# -*- coding: utf-8 -*-
# Extrai os handlers de whisper/localizacao e seus consumidores no cliente.
# Saidas: C:\temp\world_whisper_location.txt,
#         C:\temp\engine_whisper_location.txt e C:\temp\rakion_whisper_location.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x00420200, "C->S 0x16 whisper"),
        (0x00420410, "C->S 0x17 WhereAmI"),
        (0x00420520, "C->S 0x18 WhereAreYou"),
        (0x0040AF20, "comparacao exata de CharName"),
        (0x0040AF90, "le ChannelId e ChannelSlot"),
        (0x0040B7D0, "le FieldId e FieldSeat"),
        (0x0042CEE0, "carrega ServerId em World+0x54"),
    ),
    "engine.dll": (
        (0x36193000, "parser S->C 0x16"),
        (0x361930B0, "parser S->C 0x17"),
        (0x361930F0, "parser S->C 0x18"),
    ),
    "rakion.bin": (
        (0x00475A30, "callback UI do 0x16"),
        (0x00475D80, "callback UI do 0x17"),
        (0x00475F10, "callback UI do 0x18"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_whisper_location.txt",
    "engine.dll": r"C:\temp\engine_whisper_location.txt",
    "rakion.bin": r"C:\temp\rakion_whisper_location.txt",
}


def decompile(function, decompiler, monitor):
    result = decompiler.decompileFunction(function, 180, monitor)
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

print("whisper/localizacao extraidos: %s" % output_path)
