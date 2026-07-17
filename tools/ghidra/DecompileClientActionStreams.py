# -*- coding: utf-8 -*-
# Extrai codecs e aplicação dos streams 0x030A/0x030F/0x0311 do engine.dll v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x360FBCC0,  # CEntityMessage::WritePlayerAction
    0x360FBD20,  # CEntityMessage::ReadPlayerAction
    0x360FF750,  # bind do canal P2P
    0x360FFB10,  # send do canal P2P
    0x361001F0,  # demux 0x4000/0x0304/0x0305/0x0319
    0x361017F0,  # CPlayerAction::Normalize
    0x36101820,  # serializer compacto de CPlayerAction
    0x36101830,  # parser compacto de CPlayerAction
    0x36101D10,  # CPlayerAction::Clear
    0x36101D90,  # CPlayerAction::CPlayerAction
    0x36101DA0,  # CPlayerAction::Lerp
    0x36102FA0,  # CPlayerSource::SetAction
    0x36103940,  # CPlayerSource::SendAction
    0x36103CB0,  # SendAction relay
    0x3610AFE0,  # CSessionState::GetActionFromMessage
    0x3610CD20,  # loop que emite ações
    0x3610D7C0,  # CSessionState::HandleMessage
    0x361AA780,  # CPlayerEntity::ApplyAction
    0x361AA790,  # CPlayerEntity::UpdatePlacement
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\client_action_streams.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionContaining(toAddr(address))
        if function is None:
            output.write("\n===== ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("streams de ação do cliente extraídos")
