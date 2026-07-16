# -*- coding: utf-8 -*-
# Extrai eventos, efeitos e recurso Chaos ligados ao uso de poções em entitiesmp.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x35130710,  # EUsePotion::EUsePotion
    0x35130D30,  # CPlayer::HandleEvent
    0x35134FE0,  # EUsePotion::MakeCopy
    0x35135180,  # CPlayer::UseHPPotion
    0x351351E0,  # CPlayer::UseSteamPotion
    0x35135250,  # CPlayer::UseHorroPotion1
    0x351352C0,  # CPlayer::UseHorroPotion2
    0x35135330,  # CPlayer::UseAPPotion
    0x351353A0,  # CPlayer::UseCPPotion
    0x35135410,  # CPlayer::UseChaosPotion
    0x351408D0,  # CPlayer::UseScouterPotion
    0x35143040,  # CPlayer::IncreaseChaosPoint(short)
    0x35143210,  # CPlayer::IsChargeChaosPoint
    0x35143290,  # CPlayer::IncreaseChaosPoint(int)
    0x3514A8B0,  # CPlayer::StartRound (limpa Steam e Scouter)
    0x35152DA0,  # CPlayer::ReceiveDamage (consome o estado Steam)
    0x3515A7F0,  # CPlayer::ChaosProc
    0x3515E830,  # CPlayer::Death (limpa o estado Scouter)
    0x35162970,  # CPlayer::Main
    0x35163420,  # handler do estado CPlayer::Main
    0x35164791,  # ramo EUsePotion dentro do handler
    0x35164885,  # HP
    0x351648CA,  # Steam
    0x35164A1F,  # Horror 1
    0x35164A75,  # Horror 2
    0x35164ACB,  # AP
    0x35164B10,  # Scouter
    0x35164B5B,  # CP
    0x35164B9D,  # Chaos
    0x35219210,  # ClearToDefault(EUsePotion)
)
OUTPUT = r"C:\temp\client_potion_effects.txt"

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

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
        if caller is not None:
            functions[str(caller.getEntryPoint())] = caller

with open(OUTPUT, "w") as output:
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("efeitos client de pocoes extraidos em " + OUTPUT)
