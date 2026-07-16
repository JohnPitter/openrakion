# -*- coding: utf-8 -*-
# Rastreia o clanId da presença de canal até a UI e a transição para a sala.
# Saídas: C:\temp\client_clan_presence_engine.txt e
#         C:\temp\client_clan_presence_rakion.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "engine.dll": (
        (0x36197320, "dispatcher World S->C"),
        (0x361932A0, "0x1E snapshot de presenca"),
        (0x361933F0, "0x1F entrada incremental"),
        (0x36196CE0, "0x37 snapshot da sala"),
        (0x361970E0, "0x38 entrada na sala"),
    ),
    "rakion.bin": (
        (0x00472BF0, "callback 0x1E"),
        (0x00473DA0, "callback 0x1F"),
        (0x0040CBF0, "cria linhas do snapshot"),
        (0x0040CC40, "cria linha incremental"),
        (0x0042EAA0, "aloca linha de presenca"),
        (0x0043F700, "copia registro de 0x24 bytes"),
        (0x0043F740, "evento da linha"),
        (0x0043FEE0, "desenha nome com a cor derivada do clanId"),
        (0x0047A370, "troca tela de canal pela tela de sala"),
        (0x00480B90, "copia registro de jogador da sala"),
    ),
}

OUTPUTS = {
    "engine.dll": r"C:\temp\client_clan_presence_engine.txt",
    "rakion.bin": r"C:\temp\client_clan_presence_rakion.txt",
}

CHANNEL_ROW_VTABLE = 0x004D8C78
CHANNEL_ROW_VTABLE_SLOTS = 20


def decompile(function, decompiler, monitor):
    result = decompiler.decompileFunction(function, 180, monitor)
    if result and result.decompileCompleted():
        return result.getDecompiledFunction().getC()
    return "(falha na decompilacao)"


def ascii_text(value):
    return value.encode("ascii", "replace").decode("ascii")


def write_targets(output, targets, manager, decompiler, monitor):
    for address, purpose in targets:
        function = manager.getFunctionAt(toAddr(address))
        output.write("\n===== %s @ %08x - %s =====\n" % (
            function.getName() if function else "funcao ausente",
            address,
            purpose,
        ))
        if function:
            output.write(ascii_text(decompile(function, decompiler, monitor)))


def write_channel_row_vtable(output, manager, memory, decompiler, monitor):
    output.write("\n===== vtable da linha de presenca @ %08x =====\n" % CHANNEL_ROW_VTABLE)
    seen = set()
    for slot in range(CHANNEL_ROW_VTABLE_SLOTS):
        entry = CHANNEL_ROW_VTABLE + slot * 4
        target = memory.getInt(toAddr(entry)) & 0xffffffff
        function = manager.getFunctionContaining(toAddr(target))
        name = function.getName() if function else "?"
        output.write("slot=%02d offset=+0x%02x entry=%08x target=%08x %s\n" % (
            slot, slot * 4, entry, target, name,
        ))
        if function and function.getEntryPoint().getOffset() not in seen:
            seen.add(function.getEntryPoint().getOffset())
            output.write(ascii_text(decompile(function, decompiler, monitor)))


program_name = currentProgram.getName().lower()
if program_name not in TARGETS:
    raise RuntimeError("programa nao suportado: %s" % program_name)

manager = currentProgram.getFunctionManager()
memory = currentProgram.getMemory()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUTS[program_name], "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    write_targets(output, TARGETS[program_name], manager, decompiler, monitor)
    if program_name == "rakion.bin":
        write_channel_row_vtable(output, manager, memory, decompiler, monitor)

print("rastreio de presenca de cla extraido: %s" % OUTPUTS[program_name])
