# -*- coding: utf-8 -*-
# Extrai Golden Sword, Gold Golem e eventos de Master Golem em entitiesmp.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x350DC690,  # EGoldSword::GetSizeOf
    0x350DC6A0,  # EGoldSword::EGoldSword
    0x350DC6C0,  # EGoldSword::CheckIDs
    0x350DC8E0,  # EMasterGolemDamage::EMasterGolemDamage
    0x350DC8D0,  # EMasterGolemDamage::GetSizeOf
    0x350E0A00,  # EGoldSword::MakeCopy
    0x350E0C60,  # EMasterGolemDamage::MakeCopy
    0x350E9930,  # CNpcBase::ResetGoldSwordForAllPlayers
    0x350E9A40,  # CNpcBase::SetGoldSwordModeForPlayer
    0x350FF880,  # EGoldGolemRespawn copy ctor
    0x350FF8A0,  # EGoldGolemRespawn::operator=
    0x350FFAB0,  # EGoldGolemRespawn::EGoldGolemRespawn
    0x350FFAA0,  # EGoldGolemRespawn::GetSizeOf
    0x350FFB00,  # EGoldGolemRebirth::GetSizeOf
    0x350FFB10,  # EGoldGolemRebirth::EGoldGolemRebirth
    0x350FFB40,  # CNpcGoldGolem::SetDefaultProperties
    0x35114100,  # EMasterGolemRespawn copy ctor
    0x35114260,  # EMasterGolemRespawn::EMasterGolemRespawn
    0x35114250,  # EMasterGolemRespawn::GetSizeOf
    0x351336D0,  # CPlayer::EndMasterGolems
    0x3513BC60,  # CPlayer::RenderMasterGolemHP
    0x3513E300,  # CPlayer::IsMasterGolemDead
    0x35148DD0,  # CPlayer::AquireGoldSword
    0x35148E00,  # CPlayer::RestoreGoldSword
    0x35148E30,  # CPlayer::ChangeModeGoldSword
)
OUTPUT = r"C:\temp\client_golem_objective.txt"

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
reference_lines = []

for offset in TARGETS:
    address = toAddr(offset)
    function = manager.getFunctionAt(address)
    if function is None:
        function = manager.getFunctionContaining(address)
    if function is None:
        disassemble(address)
        function = createFunction(address, None)
    if function is None:
        continue
    functions[str(function.getEntryPoint())] = function
    for reference in references.getReferencesTo(function.getEntryPoint()):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        reference_lines.append(
            "%s <- %s em %s @ %s\n" % (
                function.getEntryPoint(),
                reference.getFromAddress(),
                caller.getName() if caller else "(sem funcao)",
                caller.getEntryPoint() if caller else "-",
            )
        )
        if caller is not None:
            functions[str(caller.getEntryPoint())] = caller

with open(OUTPUT, "w") as output:
    output.write("===== referencias =====\n")
    for line in reference_lines:
        output.write(line)
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("objetivos Golem do cliente extraidos em " + OUTPUT)
