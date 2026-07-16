# -*- coding: utf-8 -*-
# Extrai o fluxo 0x31 InventoryMove nas tres pontas.
# Saidas: C:\temp\world_inventory_move.txt, C:\temp\engine_inventory_move.txt e
#         C:\temp\rakion_inventory_move.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x00421870, "handler C->S 0x31 InventoryMove"),
        (0x0040CF10, "regra de swap entre box e zona ativa"),
        (0x0040BC10, "compatibilidade de item com slot ativo"),
    ),
    "engine.dll": (
        (0x36191810, "builder C->S 0x31 InventoryMove"),
        (0x36193810, "parser S->C 0x31 InventoryMove"),
    ),
    "rakion.bin": (
        (0x0047D1D0, "callback UI S->C 0x31 InventoryMove"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_inventory_move.txt",
    "engine.dll": r"C:\temp\engine_inventory_move.txt",
    "rakion.bin": r"C:\temp\rakion_inventory_move.txt",
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
            function.getName() if function else "funcao ausente", address, purpose))
        if function:
            output.write(ascii_text(decompile(function, decompiler, monitor)))

print("InventoryMove extraido: %s" % output_path)
