# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia Nak na build v258.
# @category Rakion
import struct

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


VARIANT_DESCRIPTORS = (
    0x3538BF98,
    0x3538BFF8,
    0x3538C058,
    0x3538C0B8,
)

SET_DEFAULT_ENTRY_POINTS = (
    0x35117D30,
    0x35118140,
    0x35118470,
    0x351187A0,
    0x35118AD0,
)

NAK_EVENT_TABLE = 0x3538C0E8
NAK_EVENT_COUNT = 29
NAK_DEFAULT_EVENT = 0x3538C2B8
NPC_BASE_EVENT_TABLE = 0x35389FB8
NPC_BASE_EVENT_COUNT = 102

ASSET_STRINGS = (
    0x352D7CE8,  # NpcNak1
    0x352D7CF4,  # summon sound
    0x352D7D18,  # death sound
    0x352D7D38,  # attacked sound
    0x352D7D5C,  # poison shot sound
    0x352D7D80,  # Move_Fast
    0x352D7D8C,  # Shoot_Poison
    0x352D7D9C,  # model
)

HELPERS = (
    0x35118B00,
    0x35118B10,
    0x35118B30,
    0x35118B90,
    0x35118F50,
    0x351191B0,
    0x35119DD0,
    0x35119E70,
    0x3511A520,
)


def u32(memory, address):
    return memory.getInt(toAddr(address)) & 0xffffffff


def bits_to_float(value):
    return struct.unpack("<f", struct.pack("<I", value))[0]


def ascii_string(memory, address, limit=160):
    chars = []
    cursor = toAddr(address)
    for _ in range(limit):
        value = memory.getByte(cursor) & 0xff
        if value == 0:
            break
        chars.append(chr(value) if 0x20 <= value < 0x7f else "?")
        cursor = cursor.add(1)
    return "".join(chars)


def event_record(memory, address):
    return (
        u32(memory, address),
        u32(memory, address + 4),
        u32(memory, address + 8),
        u32(memory, address + 12),
    )


def decompile(output, address):
    target = toAddr(address)
    function = manager.getFunctionContaining(target)
    if function is None and currentProgram.getMemory().contains(target):
        disassemble(target)
        createFunction(target, None)
        function = manager.getFunctionContaining(target)
    if function is None:
        output.write("\n===== 0x%08x ausente =====\n" % address)
        return
    output.write("\n===== %s @ %s =====\n" % (
        function.getName(), function.getEntryPoint()))
    result = decompiler.decompileFunction(function, 240, monitor)
    if result and result.getDecompiledFunction():
        output.write(result.getDecompiledFunction().getC())


def disassemble_entries(output, listing):
    output.write("\n===== SetDefaultProperties entry points =====\n")
    for address in SET_DEFAULT_ENTRY_POINTS:
        instruction = listing.getInstructionAt(toAddr(address))
        if instruction is None:
            disassemble(toAddr(address))
            instruction = listing.getInstructionAt(toAddr(address))
        output.write("%08x" % address)
        for _ in range(4):
            if instruction is None:
                break
            output.write(" | %s" % instruction)
            if instruction.getFlowType().isTerminal() or instruction.getFlowType().isJump():
                break
            instruction = instruction.getNext()
        output.write("\n")


if currentProgram.getName().lower() != "entitiesmp_dump.bin":
    raise ValueError("execute na imagem runtime entitiesmp_dump.bin da build v258")

args = getScriptArgs()
output_path = args[0] if args else r"C:\temp\client_npc_nak.txt"
memory = currentProgram.getMemory()
manager = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

nak_events = []
mapped_base_ids = set()
for index in range(NAK_EVENT_COUNT):
    record = event_record(memory, NAK_EVENT_TABLE + index * 16)
    nak_events.append(record)
    if record[1] != 0xffffffff:
        mapped_base_ids.add(record[1])
nak_events.append(event_record(memory, NAK_DEFAULT_EVENT))

base_handlers = {}
for index in range(NPC_BASE_EVENT_COUNT):
    record = event_record(memory, NPC_BASE_EVENT_TABLE + index * 16)
    if record[0] in mapped_base_ids:
        base_handlers[record[0]] = record[2]

with open(output_path, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    output.write("\n===== variant descriptors =====\n")
    for address in VARIANT_DESCRIPTORS:
        name_pointer = u32(memory, address)
        output.write(
            "%08x name=%s class_id=%08x parent=%08x factory=%08x\n" % (
                address,
                ascii_string(memory, name_pointer),
                u32(memory, address + 8),
                u32(memory, address + 12),
                u32(memory, address + 16),
            )
        )
    output.write("\n===== assets =====\n")
    for address in ASSET_STRINGS:
        output.write("%08x %s\n" % (address, ascii_string(memory, address)))
    output.write("\n===== scalar defaults =====\n")
    output.write("attack_range_bits=40400000 value=%s\n" % bits_to_float(0x40400000))
    output.write("Nak event table: %d local + default\n" % NAK_EVENT_COUNT)
    for event_id, base_id, handler, binder in nak_events:
        output.write(
            "event=%08x base=%08x handler=%08x binder=%08x\n" % (
                event_id, base_id, handler, binder))
    output.write("\n===== mapped CNpcBase handlers =====\n")
    for event_id in sorted(mapped_base_ids):
        output.write("event=%08x handler=%08x\n" % (
            event_id, base_handlers.get(event_id, 0)))
    disassemble_entries(output, listing)
    handlers = sorted(set(record[2] for record in nak_events))
    for address in handlers + list(HELPERS) + list(base_handlers.values()):
        decompile(output, address)

print("familia Nak extraida em %s" % output_path)
