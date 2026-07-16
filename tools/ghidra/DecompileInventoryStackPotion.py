# -*- coding: utf-8 -*-
# Extrai o fluxo 0x73 InventoryStackPotion e o ack auxiliar 0x27.
# Saidas: C:\temp\world_inventory_stack_potion.txt,
#         C:\temp\engine_inventory_stack_potion.txt e
#         C:\temp\rakion_inventory_stack_potion.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x00421A50, "handler C->S 0x73 InventoryStackPotion"),
        (0x0040C140, "valida e calcula a operacao de stack"),
        (0x0040CA50, "projeta listas de inventario do usuario"),
        (0x0040BCB0, "serializa as duas listas de inventario"),
        (0x0040BC50, "decide bloco extra do personagem"),
    ),
    "engine.dll": (
        (0x36191B40, "builder C->S 0x73 InventoryStackPotion"),
        (0x36194080, "parser S->C 0x73 erro"),
        (0x361935D0, "parser S->C 0x27 sucesso auxiliar"),
    ),
    "rakion.bin": (
        (0x004782E0, "callback UI S->C 0x73 erro"),
        (0x004756E0, "callback UI S->C 0x27 sucesso auxiliar"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_inventory_stack_potion.txt",
    "engine.dll": r"C:\temp\engine_inventory_stack_potion.txt",
    "rakion.bin": r"C:\temp\rakion_inventory_stack_potion.txt",
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

print("InventoryStackPotion extraido: %s" % output_path)
