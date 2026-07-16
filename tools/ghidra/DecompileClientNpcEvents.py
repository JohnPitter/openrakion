# -*- coding: utf-8 -*-
# Extrai construtores e copias dos eventos CNpcBase 0x044D0000..0x044D0018.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x350E0730,  # ESoulShot ctor
    0x350E0800,  # ESetTargetforGroup ctor
    0x350E08E0,  # EReportTargetforLeader ctor
    0x350DC580,  # ESpawnNpc ctor
    0x350DC650,  # ENpcBaseDeath ctor
    0x350DC6A0,  # EGoldSword ctor
    0x350DC6E0,  # ENpcDisappear ctor
    0x350E0A60,  # ENpcBaseDamage ctor
    0x350DC740,  # ENpcHP ctor
    0x350DC780,  # EAttackHit ctor
    0x350DC7C0,  # EAttackFire ctor
    0x350DC800,  # ETouchSendedByRemote ctor
    0x350DC840,  # EMovementAnimation ctor
    0x350E0C00,  # ENpcDeadToSwitch ctor
    0x350DC8E0,  # EMasterGolemDamage ctor
    0x350DC920,  # ENpcExtraSet ctor
)
OUTPUT = r"C:\temp\client_npc_events.txt"

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for value in TARGETS:
        function = manager.getFunctionAt(toAddr(value))
        if function is None:
            disassemble(toAddr(value))
            function = createFunction(toAddr(value), None)
        if function is None:
            output.write("\n===== 0x%08X sem funcao =====\n" % value)
            continue
        output.write("\n===== %s @ %s =====\n" % (
            function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())
        else:
            output.write("(falha de decompilacao)\n")

print("eventos NPC extraidos em " + OUTPUT)
