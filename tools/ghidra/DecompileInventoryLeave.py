# -*- coding: utf-8 -*-
# Extrai a maquina de estado, persistencia e consumo do InventoryLeave 0x2D.
# Saidas: C:\temp\world_inventory_leave.txt,
#         C:\temp\engine_inventory_leave.txt e C:\temp\rakion_inventory_leave.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x00420DE0, "C->S 0x2C abre inventario"),
        (0x00420F10, "C->S 0x2D fecha inventario"),
        (0x0040B000, "transicao fechado para aberto"),
        (0x0040C960, "coleta deltas e fecha inventario"),
        (0x0040BCB0, "coleta box/equipamento alterados"),
        (0x0040BC50, "coleta atributos alterados"),
        (0x0041B940, "enfileira comando DB"),
        (0x00414CC0, "worker do comando DB 0x12"),
        (0x00427A80, "callback do comando DB 0x12"),
        (0x00419730, "worker do comando DB 0x13"),
        (0x0041CCA0, "callback do comando DB 0x13"),
    ),
    "engine.dll": (
        (0x36191700, "builder C->S 0x2D"),
        (0x36193650, "parser S->C 0x2C"),
        (0x36193680, "parser S->C 0x2D"),
    ),
    "rakion.bin": (
        (0x00474C70, "callback UI S->C 0x2C"),
        (0x00474DE0, "callback UI S->C 0x2D"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_inventory_leave.txt",
    "engine.dll": r"C:\temp\engine_inventory_leave.txt",
    "rakion.bin": r"C:\temp\rakion_inventory_leave.txt",
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

print("InventoryLeave extraido: %s" % output_path)
