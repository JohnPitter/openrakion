# -*- coding: utf-8 -*-
# Extrai os tres pares de serializers polimorficos de init de NPC em entitiesmp.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x350E3FA0,  # CNpcBase::GetInitData
    0x350E96E0,  # CNpcBase::ApplyInitData
    0x350F46F0,  # CNpcChocolateCake::SetDefaultProperties
    0x35100DC0,  # CNpcGoldGolem::GetInitData
    0x35100EB0,  # CNpcGoldGolem::ApplyInitData
    0x350F4EE0,  # CNpcChocolateCake::GetInitData
    0x350F4FE0,  # CNpcChocolateCake::ApplyInitData
    0x350FFB40,  # CNpcGoldGolem::SetDefaultProperties
)
OUTPUT = r"C:\temp\client_entity_init_serializers.txt"
IMPORTS = (
    (0x352B3A54, 0x36004B00, "CNetMessage::operator<<(float)"),
    (0x352B3AB8, 0x36004A00, "CNetMessage::operator>>(float)"),
    (0x352B362C, 0x36004B80, "CNetMessage::operator<<(u8)"),
    (0x352B393C, 0x36004A80, "CNetMessage::operator>>(u8)"),
    (0x352B3A7C, 0x36004BA0, "CNetMessage::operator<<(u32)"),
    (0x352B3AB4, 0x36004AA0, "CNetMessage::operator>>(u32)"),
    (0x352B3A80, 0x3611FB40, "CEntity::GetEntityClassInfo"),
    (0x352B3674, 0x361094E0, "CSessionState::GetEntity"),
    (0x352B36F8, 0x36001000, "CTString::Length"),
    (0x352B33F0, 0x3601FF50, "CTString::operator!=(char const*)"),
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    output.write("===== imports runtime tipados =====\n")
    for slot, expected, name in IMPORTS:
        actual = currentProgram.getMemory().getInt(toAddr(slot)) & 0xffffffff
        status = "ok" if actual == expected else "DIVERGENTE"
        output.write("0x%08X -> 0x%08X expected=0x%08X %s %s\n" % (
            slot, actual, expected, status, name))
    for value in TARGETS:
        address = toAddr(value)
        function = manager.getFunctionAt(address)
        if function is None:
            disassemble(address)
            function = createFunction(address, None)
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

print("serializers de init extraidos em " + OUTPUT)
