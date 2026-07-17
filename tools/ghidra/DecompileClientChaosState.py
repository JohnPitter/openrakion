# -*- coding: utf-8 -*-
# Extrai a máquina de estado Chaos e resolve os ponteiros importados do dump v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x35139D60,  # CPlayer::SetModelsColor
    0x351413C0,  # CPlayer::GetHitPower
    0x35143040,  # CPlayer::IncreaseChaosPoint(short)
    0x35143210,  # CPlayer::IsChargeChaosPoint
    0x35143290,  # CPlayer::IncreaseChaosPoint(int)
    0x351471E0,  # CPlayer::SetupDownState
    0x35147320,  # CPlayer::GetDamageAnimName
    0x35147D30,  # CPlayer::GetMoveSpeed
    0x35148B90,  # CPlayer::GetModeName
    0x3514A040,  # CPlayer::SetArmor
    0x3514C450,  # CPlayer::SetModelsOriginalColor
    0x3514FEA0,  # CPlayer::ReceiveDamage_OnGuard
    0x35150770,  # CPlayer::ApplyReceiveDamage
    0x35152DA0,  # CPlayer::ReceiveDamage
    0x3515A7F0,  # CPlayer::ChaosProc(float)
    0x3515C480,  # CPlayer::ChangeMode
    0x3515E830,  # CPlayer::Death
    0x35171F50,  # CPlayerAnimator::AnimateAttack_Chaos
    0x3517B020,  # CPlayerWeapons::GetDamageMotionType
    0x3517B1E0,  # CPlayerWeapons::SetupDamageInfo
    0x3517D3D0,  # CPlayerWeapons::UpdateWeaponHit
)

POINTERS = (
    0x352B300C,
    0x352B3080,
    0x352B30A0,
    0x352B32D8,
    0x352B32DC,
    0x352B3350,
    0x352B34BC,
    0x352B35B4,
    0x352B3968,
    0x352B4280,
)

CONSTANTS = (
    0x352DDC10,
)

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
memory = currentProgram.getMemory()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\client_chaos_state.txt", "w") as output:
    output.write("===== pointers =====\n")
    for address in POINTERS:
        output.write("%08x -> %08x\n" % (address, memory.getInt(toAddr(address)) & 0xffffffff))
    output.write("===== constants =====\n")
    for address in CONSTANTS:
        output.write("%08x bits=%08x\n" % (address, memory.getInt(toAddr(address)) & 0xffffffff))
    for address in TARGETS:
        target = toAddr(address)
        function = manager.getFunctionContaining(target)
        if function is None and memory.contains(target):
            disassemble(target)
            createFunction(target, None)
            function = manager.getFunctionContaining(target)
        if function is None:
            continue
        callers = []
        for reference in references.getReferencesTo(function.getEntryPoint()):
            caller = manager.getFunctionContaining(reference.getFromAddress())
            if caller is not None:
                callers.append("%s@%s" % (caller.getName(), caller.getEntryPoint()))
        output.write("\n===== %s @ %s callers=%s =====\n" % (
            function.getName(), function.getEntryPoint(), ",".join(callers)))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())
        output.write("\n----- disassembly -----\n")
        instructions = currentProgram.getListing().getInstructions(function.getBody(), True)
        while instructions.hasNext():
            instruction = instructions.next()
            output.write("%s | %s\n" % (instruction.getAddress(), instruction))

print("maquina Chaos extraida")
