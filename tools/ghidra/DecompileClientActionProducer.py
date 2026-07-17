# -*- coding: utf-8 -*-
# Extrai o produtor e os consumidores de CPlayerAction no gameplay original.
# @category Rakion
import os

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


SYMBOL_FRAGMENTS = (
    "ctl_ComposeActionPacket",
    "ApplyAction@CPlayer",
    "UpdatePlacement@CPlayer",
    "ActiveActions@CPlayer",
    "AliveActions@CPlayer",
    "ButtonsActions@CPlayer",
    "WaitActions@CPlayer",
)
CONTROL_OFFSETS = (0xAB0, 0xAB4, 0xAB8)
KNOWN_FUNCTIONS = {
    "entitiesmp_dump.bin": (
        0x35139310,  # ctl_ComposeActionPacket
        0x3513D5F0,  # CPlayer::UpdatePlacement
        0x35151300,  # CPlayer::ActiveActions
        0x351535E0,  # CPlayer::AliveActions
        0x35153DE0,  # CPlayer::ApplyAction
    ),
    "gamemp.dll": (
        0x100137B0,  # wrapper que chama ctl_ComposeActionPacket
    ),
}


def add_function(functions, function):
    if function is not None:
        functions[str(function.getEntryPoint())] = function


symbols = currentProgram.getSymbolTable()
references = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
monitor = ConsoleTaskMonitor()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
functions = {}
matches = []
offset_hits = []

for address in KNOWN_FUNCTIONS.get(currentProgram.getName(), ()):
    add_function(functions, manager.getFunctionContaining(toAddr(address)))

for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True)
    if not any(fragment.lower() in name.lower() for fragment in SYMBOL_FRAGMENTS):
        continue
    matches.append(symbol)
    add_function(functions, manager.getFunctionContaining(symbol.getAddress()))
    for reference in references.getReferencesTo(symbol.getAddress()):
        add_function(functions, manager.getFunctionContaining(reference.getFromAddress()))

instructions = listing.getInstructions(True)
while instructions.hasNext() and not monitor.isCancelled():
    instruction = instructions.next()
    matched_offset = None
    for operand_index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(operand_index):
            if hasattr(obj, "getValue") and obj.getValue() in CONTROL_OFFSETS:
                matched_offset = obj.getValue()
                break
        if matched_offset is not None:
            break
    if matched_offset is None:
        continue
    owner = manager.getFunctionContaining(instruction.getAddress())
    offset_hits.append((matched_offset, instruction, owner))
    add_function(functions, owner)

program_name = currentProgram.getName().replace(".", "_")
output_path = os.path.join(r"C:\temp", program_name + "_action_producer.txt")
with open(output_path, "w") as output:
    output.write("PROGRAM=%s SYMBOLS=%d OFFSET_HITS=%d FUNCTIONS=%d\n" % (
        currentProgram.getName(), len(matches), len(offset_hits), len(functions)))
    for symbol in matches:
        output.write("SYMBOL %s @ %s\n" % (symbol.getName(True), symbol.getAddress()))
    for offset, instruction, owner in offset_hits:
        owner_name = owner.getName() if owner is not None else "<sem funcao>"
        output.write("OFFSET 0x%x %s %s :: %s\n" % (
            offset, instruction.getAddress(), owner_name, instruction))
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            code = result.getDecompiledFunction().getC()
            output.write(code.encode("ascii", "replace").decode("ascii"))

print("produtor de CPlayerAction extraido em " + output_path)
