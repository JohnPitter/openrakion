# -*- coding: utf-8 -*-
# Localiza produtores candidatos do frame S->C 0x0C da lista de personagens.
# Saida: C:\temp\world_char_list_producer.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor


TARGET = 0x0C
SENDERS = ("FUN_004038e0", "FUN_004061f0", "FUN_0041b8a0", "FUN_0041b880")
OUTPUT = r"C:\temp\world_char_list_producer.txt"

listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
monitor = ConsoleTaskMonitor()
sender_addresses = {}
functions = manager.getFunctions(True)
while functions.hasNext():
    function = functions.next()
    if function.getName() in SENDERS:
        sender_addresses[function.getEntryPoint().getOffset()] = function.getName()


def find_sender_path(function, remaining, visited):
    address = function.getEntryPoint().getOffset()
    if address in sender_addresses:
        return [address]
    if remaining == 0 or address in visited:
        return None
    visited = set(visited)
    visited.add(address)
    called = function.getCalledFunctions(monitor).iterator()
    while called.hasNext():
        callee = called.next()
        path = find_sender_path(callee, remaining - 1, visited)
        if path:
            return [address] + path
    return None


candidates = {}
instructions = listing.getInstructions(True)
while instructions.hasNext():
    instruction = instructions.next()
    address = instruction.getAddress().getOffset()
    if address < 0x00400000 or address >= 0x00430000:
        continue
    for index in range(instruction.getNumOperands()):
        representation = instruction.getDefaultOperandRepresentation(index).lower().replace(" ", "")
        if representation != "0xc":
            continue
        if not any(isinstance(obj, Scalar) and obj.getUnsignedValue() == TARGET
                   for obj in instruction.getOpObjects(index)):
            continue
        function = manager.getFunctionContaining(instruction.getAddress())
        if function is not None:
            candidates.setdefault(function.getEntryPoint().getOffset(), []).append(
                (address, instruction.toString()))

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
matches = []
for address in sorted(candidates):
    function = manager.getFunctionAt(toAddr(address))
    path = find_sender_path(function, 5, set())
    if not path:
        continue
    result = decompiler.decompileFunction(function, 180, monitor)
    if result and result.getDecompiledFunction():
        matches.append((address, path, candidates[address], result.getDecompiledFunction().getC()))

with open(OUTPUT, "w") as output:
    output.write("literal=0x0C candidates=%d\n" % len(matches))
    for address, path, occurrences, body in matches:
        output.write("\n===== 0x%08X =====\n" % address)
        output.write("sender_path=%s\n" % " -> ".join("0x%08X" % value for value in path))
        for instruction_address, instruction_text in occurrences:
            output.write("literal 0x%08X instruction=%s\n" %
                         (instruction_address, instruction_text))
        output.write(body.encode("ascii", "replace").decode("ascii"))

print("candidatos ao produtor S->C 0x0C: %d" % len(matches))
