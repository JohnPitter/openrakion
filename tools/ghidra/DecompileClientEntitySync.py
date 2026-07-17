# -*- coding: utf-8 -*-
# Extrai criação, eventos e sincronização de entidades do engine.dll v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
import jarray


ADDRESSES = (
    0x361095F0,  # CSessionState::GetMasterGolem
    0x361098E0,  # CSessionState::AddRemoteMasterGolem
    0x36109BE0,  # CSessionState::SendInfoCreateNpcTo
    0x36109CC0,  # CSessionState::SendInfoCreateMapNpcTo
    0x3610B1E0,  # CSessionState::SendInfoCreateMasterGolemTo
    0x3610C7C0,  # CSessionState::CreateMasterGolem
    0x3610C8C0,  # CSessionState::DestroyMasterGolems
    0x3610D060,  # CSessionState::BuildMapItemList
    0x3610D6A0,  # CSessionState::SendInfoMapItemStatus
    0x3610D7C0,  # CSessionState::HandleMessage 0x0307..0x0312
)

REFERENCE_TARGETS = (
    0x361098E0,
    0x3610B1E0,
    0x36229BBC,  # constante usada para emitir 0x030B
)

MESSAGE_CONSTANTS = (
    0x36229BB0,
    0x36229BB4,
    0x36229BB8,
    0x36229BBC,
    0x36229BC0,
)

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()


def decompile(function):
    result = decompiler.decompileFunction(function, 180, monitor)
    if result and result.getDecompiledFunction():
        return result.getDecompiledFunction().getC()
    return "(falha)"


functions = {}
for address in ADDRESSES:
    function = manager.getFunctionContaining(toAddr(address))
    if function:
        functions[str(function.getEntryPoint())] = function

for function in manager.getFunctions(True):
    name = function.getName()
    if ("SendPacket_Reliable" in name or "PackFloatToSWord" in name or
            "UnpackFloatToSWord" in name or "MsgEvent" in name):
        functions[str(function.getEntryPoint())] = function

reference_lines = []
for target in REFERENCE_TARGETS:
    for reference in references.getReferencesTo(toAddr(target)):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        caller_name = caller.getName() if caller else "(sem funcao)"
        reference_lines.append(
            "%08x <- %s em %s @ %s\n" % (
                target,
                reference.getFromAddress(),
                caller_name,
                caller.getEntryPoint() if caller else "-",
            )
        )
        if caller:
            functions[str(caller.getEntryPoint())] = caller

message_030b_lines = []
pattern = jarray.array([0x0b, 0x03], "b")
memory = currentProgram.getMemory()
for block in memory.getBlocks():
    if not block.isInitialized():
        continue
    cursor = block.getStart()
    while cursor and cursor.compareTo(block.getEnd()) <= 0:
        hit = memory.findBytes(cursor, block.getEnd(), pattern, None, True, monitor)
        if hit is None:
            break
        refs = list(references.getReferencesTo(hit))
        if refs:
            for reference in refs:
                caller = manager.getFunctionContaining(reference.getFromAddress())
                message_030b_lines.append(
                    "%s <- %s em %s @ %s\n" % (
                        hit,
                        reference.getFromAddress(),
                        caller.getName() if caller else "(sem funcao)",
                        caller.getEntryPoint() if caller else "-",
                    )
                )
                if caller:
                    functions[str(caller.getEntryPoint())] = caller
        cursor = hit.add(1)

with open(r"C:\temp\client_entity_sync.txt", "w") as output:
    output.write("===== constantes =====\n")
    for address in MESSAGE_CONSTANTS:
        value = currentProgram.getMemory().getShort(toAddr(address)) & 0xffff
        output.write("%08x = 0x%04x\n" % (address, value))
    output.write("===== literais 0x030b referenciados =====\n")
    for line in message_030b_lines:
        output.write(line)
    output.write("===== referencias =====\n")
    for line in reference_lines:
        output.write(line)
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(decompile(function).encode("ascii", "replace").decode("ascii"))

print("sincronizacao de entidades do cliente extraida")
