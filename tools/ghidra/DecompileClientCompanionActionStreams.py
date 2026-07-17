# -*- coding: utf-8 -*-
# Extrai produtores e consumidores dos streams P2P 0x030F/0x0311 da build v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "engine.dll": (
        0x36103040,  # CPlayerSource::SendSyncData
        0x3610D7C0,  # CSessionState::HandleMessage
    ),
    "rakion_orig.exe": (
        0x00411760,  # dispatcher externo de mensagens P2P
    ),
    "entitiesmp_dump.bin": (
        0x3513A200,  # CPlayer::GetSyncData
        0x3514CA80,  # CPlayer::ApplySyncData
        0x35152990,  # CPlayer::DoAnimPacket
        0x3513E570,  # CPlayer::ExecNormalAnim
        0x3514A5F0,  # CPlayer::ExecAttackAnim
        0x3514A6C0,  # CPlayer::ExecDamageAnim
    ),
    "entitiesmp.dll": (
        0x3513A200,  # CPlayer::GetSyncData
        0x3514CA80,  # CPlayer::ApplySyncData
        0x35152990,  # CPlayer::DoAnimPacket
        0x3513E570,  # CPlayer::ExecNormalAnim
        0x3514A5F0,  # CPlayer::ExecAttackAnim
        0x3514A6C0,  # CPlayer::ExecDamageAnim
    ),
}

SYMBOL_FRAGMENTS = (
    "GetSyncData@CPlayer",
    "ApplySyncData@CPlayer",
    "DoAnimPacket@CPlayer",
    "ExecNormalAnim@CPlayer",
    "ExecAttackAnim@CPlayer",
    "ExecDamageAnim@CPlayer",
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for address in TARGETS.get(currentProgram.getName(), ()):
    target = toAddr(address)
    function = manager.getFunctionContaining(target)
    if function is None and currentProgram.getMemory().contains(target):
        disassemble(target)
        createFunction(target, None)
        function = manager.getFunctionContaining(target)
    if function is not None:
        functions[str(function.getEntryPoint())] = function

for function in manager.getFunctions(True):
    if any(fragment in function.getName() for fragment in SYMBOL_FRAGMENTS):
        functions[str(function.getEntryPoint())] = function

output_path = r"C:\temp\client_companion_action_streams_%s.txt" % currentProgram.getName()
with open(output_path, "w") as output:
    output.write("program=%s functions=%d\n" % (currentProgram.getName(), len(functions)))
    for key in sorted(functions):
        function = functions[key]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))
        output.write("\n----- disassembly -----\n")
        instructions = currentProgram.getListing().getInstructions(function.getBody(), True)
        while instructions.hasNext():
            instruction = instructions.next()
            output.write("%s | %s\n" % (instruction.getAddress(), instruction))

print("streams companheiros extraidos em %s" % output_path)
