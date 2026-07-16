# -*- coding: utf-8 -*-
# Extrai o contrato completo do CharacterSelect 0x14 no World e no cliente.
# Saidas: C:\temp\world_character_select.txt,
#         C:\temp\engine_character_select.txt e C:\temp\rakion_character_select.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x0041FEF0, "handler C->S 0x14"),
        (0x0040BE30, "aplica personagem selecionado na sessao"),
        (0x0040D3F0, "recalcula atributos derivados"),
        (0x0040AC30, "ativa identidade do personagem"),
        (0x0041B8B0, "notificacoes posteriores a selecao"),
    ),
    "engine.dll": (
        (0x36190E20, "builder C->S 0x14"),
        (0x36192F90, "parser S->C 0x14"),
    ),
    "rakion.bin": (
        (0x0047CB40, "callback UI S->C 0x14"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_character_select.txt",
    "engine.dll": r"C:\temp\engine_character_select.txt",
    "rakion.bin": r"C:\temp\rakion_character_select.txt",
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

print("CharacterSelect extraido: %s" % output_path)
