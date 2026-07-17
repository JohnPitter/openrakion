# -*- coding: utf-8 -*-
# Extrai o loader e os consumidores das curvas de atributos de NPC da build v258.
# @category Rakion
import re

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS_BY_PROGRAM = {
    "rakion_orig.exe": (
        0x00451DE0,  # painel de atributos da cell, language IDs 466..475
        0x00454BD0,  # barra de EXP acumulada da cell
    ),
    "entitiesmp_dump.bin": (
        0x350E1770,  # CNpcBase::GetBasicStatus
        0x350E9550,  # CNpcBase::FillDamageInfoParam
        0x350EE230,  # morte do NPC e recompensa CP
        0x351DD710,  # GetNpcSetupData
        0x35228D10,  # loader das 24 séries de creatures.dat
    ),
    "entitiesmp.dll": (
        0x350E1770,
        0x350E9550,
        0x350EE230,
        0x351DD710,
        0x35228D10,
    ),
}

RANGES_BY_PROGRAM = {
    "rakion_orig.exe": (
        (0x00451E20, 0x00452480),
    ),
    "entitiesmp_dump.bin": (
        (0x350E1770, 0x350E1790),
        (0x350E9550, 0x350E96A0),
        (0x35228D10, 0x35229620),
    ),
    "entitiesmp.dll": (),
}

CLIENT_FORMAT_STRINGS = (
    0x004DAB88, 0x004DAB8C, 0x004DAB90, 0x004DAB94,
    0x004DAB9C, 0x004DABA4, 0x004DABAC, 0x004DABB4,
    0x004DABBC, 0x004DABC0, 0x004DABC8,
)


def ascii_string(memory, address):
    chars = []
    cursor = toAddr(address)
    for _ in range(64):
        value = memory.getByte(cursor) & 0xff
        if value == 0:
            break
        chars.append(chr(value) if 0x20 <= value < 0x7f else "?")
        cursor = cursor.add(1)
    return "".join(chars)


program_name = currentProgram.getName().lower()
targets = TARGETS_BY_PROGRAM.get(program_name)
if targets is None:
    raise ValueError("programa sem catálogo de atributos NPC: %s" % currentProgram.getName())

args = getScriptArgs()
safe_name = re.sub(r"[^A-Za-z0-9_.-]", "_", program_name)
output_path = args[0] if args else r"C:\temp\client_npc_stats_%s.txt" % safe_name
manager = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
memory = currentProgram.getMemory()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(output_path, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    if program_name == "rakion_orig.exe":
        output.write("\n===== format strings =====\n")
        for address in CLIENT_FORMAT_STRINGS:
            output.write("%08x %s\n" % (address, ascii_string(memory, address)))
    for address in targets:
        target = toAddr(address)
        function = manager.getFunctionContaining(target)
        if function is None and memory.contains(target):
            disassemble(target)
            createFunction(target, None)
            function = manager.getFunctionContaining(target)
        if function is None:
            output.write("\n===== 0x%08x ausente =====\n" % address)
            continue
        output.write("\n===== %s @ %s =====\n" % (
            function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())
    for start, end in RANGES_BY_PROGRAM[program_name]:
        output.write("\n===== disassembly %08x:%08x =====\n" % (start, end))
        instruction = listing.getInstructionAt(toAddr(start))
        if instruction is None:
            disassemble(toAddr(start))
            instruction = listing.getInstructionAt(toAddr(start))
        while instruction is not None and instruction.getAddress().getOffset() <= end:
            output.write("%s | %s\n" % (instruction.getAddress(), instruction))
            instruction = instruction.getNext()

print("atributos de NPC extraídos em %s" % output_path)
