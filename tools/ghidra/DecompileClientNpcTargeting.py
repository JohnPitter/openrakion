# -*- coding: utf-8 -*-
# Extrai ownership, relações de time, validação de dano e seleção de alvo dos NPCs v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x350DD3C0,  # CNpcBase::IsValidReceiveDamage(Entity*, float)
    0x350E17C0,  # CNpcBase::IsHaveMasterPlayer
    0x350E24C0,  # CNpcBase::IsValidForEnemy
    0x350E2540,  # CNpcBase::SetPriorTarget
    0x350E25B0,  # CNpcBase::CheckValidPriorTarget
    0x350E2610,  # CNpcBase::SetTarget
    0x350E26B0,  # CNpcBase::SetSpawnVariable
    0x350E5130,  # CNpcBase::IsValidReceiveDamage(Entity*)
    0x3512ADE0,  # CNpcWatcher::evaluate
    0x3512AE30,  # CNpcWatcher::GetEntityPropertyValue
    0x3512B530,  # CNpcWatcher::SetDefaultProperties
    0x3512B5F0,  # CNpcWatcher::FindClosestFlyingPlayerInSector
    0x3512B800,  # CNpcWatcher::SendWatchEvent
    0x3512B890,  # CNpcWatcher::SetWatchDelays
    0x3512B900,  # CNpcWatcher::Main
    0x3512BA30,  # CNpcWatcher::FindClosestPlayerInWorld
    0x3512BC60,  # CNpcWatcher::FindClosestInSector_FirstNpc
    0x3512BEB0,  # CNpcWatcher::FindClosestPlayerInSector
    0x3512C130,  # CNpcWatcher::Watch
    0x351DDD30,  # IsRedTeam
    0x351DDD60,  # IsBlueTeam
    0x351DDDA0,  # IsGrayTeam
    0x351DDDD0,  # IsEnemy
)

IMPORT_POINTERS = (
    0x352B30B4,  # CListHead::IterationHead
    0x352B30B8,  # CListNode::IterationSucc
    0x352B30BC,  # CListNode::IsTailMarker
    0x352B30E4,  # CEntity::RemReference
    0x352B30E8,  # CEntity::AddReference
    0x352B3314,  # CEntity::GetFlags
    0x352B3344,  # IsDerivedFromClass(CDLLEntityClass*)
    0x352B3484,  # CEntityID::operator CEntity*
    0x352B3490,  # IsOfClass(char*)
    0x352B367C,  # IsDerivedFromClass(char*)
    0x352B36A0,  # Cos
    0x352B3A74,  # CEntity::IsDead
    0x352B3B18,  # CRelationLnk::GetDst
)

SCALARS = (
    0x352B4470,  # -1.0f
    0x352B4474,  # 1.0f
    0x352B4478,  # 0.0f
    0x352B49CC,  # 0.1f, limite inferior recebido pelo watcher
    0x352DA964,  # 179.0f, limite superior recebido pelo watcher
)

STRINGS = (
    0x352BA630,  # Player
    0x352BBB2C,  # NpcBase
    0x352BB63C,  # BoxItem
    0x352CCAAC,  # MapItem
    0x352D08D4,  # NpcIceWindBase
    0x352D0A74,  # NoDamage_Switch
    0x352D0A84,  # NpcChocolateCake
    0x352D5F90,  # NpcLongBowBase
)


def ascii_string(memory, address):
    chars = []
    cursor = toAddr(address)
    for _ in range(128):
        value = memory.getByte(cursor) & 0xff
        if value == 0:
            break
        chars.append(chr(value) if 0x20 <= value < 0x7f else "?")
        cursor = cursor.add(1)
    return "".join(chars)


manager = currentProgram.getFunctionManager()
memory = currentProgram.getMemory()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
args = getScriptArgs()
output_path = args[0] if args else r"C:\temp\client_npc_targeting.txt"

with open(output_path, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    output.write("\n===== imports =====\n")
    for address in IMPORT_POINTERS:
        output.write("%08x -> %08x\n" % (
            address, memory.getInt(toAddr(address)) & 0xffffffff))
    output.write("\n===== scalars =====\n")
    for address in SCALARS:
        output.write("%08x bits=%08x\n" % (
            address, memory.getInt(toAddr(address)) & 0xffffffff))
    output.write("\n===== strings =====\n")
    for address in STRINGS:
        output.write("%08x %s\n" % (address, ascii_string(memory, address)))
    for address in TARGETS:
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
        output.write("\n----- disassembly -----\n")
        instructions = currentProgram.getListing().getInstructions(function.getBody(), True)
        while instructions.hasNext():
            instruction = instructions.next()
            output.write("%s | %s\n" % (instruction.getAddress(), instruction))

print("targeting de NPC extraido em %s" % output_path)
