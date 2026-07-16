# -*- coding: utf-8 -*-
# Localiza produtores World das respostas S->C simples ou dormentes.
# Saida: C:\temp\world_simple_response_producers.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (0x5C, 0x63, 0x67, 0x68, 0x69, 0x6A)
SENDERS = ("FUN_004038e0", "FUN_004061f0", "FUN_0041b8a0", "FUN_0041b880")
OUTPUT = r"C:\temp\world_simple_response_producers.txt"

listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
monitor = ConsoleTaskMonitor()
candidates = {}
occurrences = {}
instructions = listing.getInstructions(True)
while instructions.hasNext():
    instruction = instructions.next()
    address = instruction.getAddress().getOffset()
    if address < 0x00400000 or address >= 0x00430000:
        continue
    value_kinds = {}
    for index in range(instruction.getNumOperands()):
        representation = instruction.getDefaultOperandRepresentation(index).lower().replace(" ", "")
        for obj in instruction.getOpObjects(index):
            if isinstance(obj, Scalar) and obj.getUnsignedValue() in TARGETS:
                value = obj.getUnsignedValue()
                kind = "literal" if representation == "0x%x" % value else "addressing"
                value_kinds.setdefault(value, set()).add(kind)
    values = set(value_kinds)
    if not values:
        continue
    function = manager.getFunctionContaining(instruction.getAddress())
    if function is None:
        continue
    key = function.getEntryPoint().getOffset()
    candidates.setdefault(key, set()).update(values)
    occurrences.setdefault(key, []).append(
        (address, value_kinds, instruction.toString()))

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


decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
matches = []
for address in sorted(candidates):
    function = manager.getFunctionAt(toAddr(address))
    path = find_sender_path(function, 4, set())
    if not path:
        continue
    result = decompiler.decompileFunction(function, 180, monitor)
    if not result or not result.getDecompiledFunction():
        continue
    body = result.getDecompiledFunction().getC()
    matches.append((address, candidates[address], path, body))

with open(OUTPUT, "w") as output:
    output.write("targets=%s candidate_functions=%d\n" %
                 (",".join("0x%02X" % value for value in TARGETS), len(matches)))
    for address, values, path, body in matches:
        output.write("\n===== 0x%08X constants=%s =====\n" %
                     (address, ",".join("0x%02X" % value for value in sorted(values))))
        output.write("sender_path=%s\n" % " -> ".join("0x%08X" % value for value in path))
        for instruction_address, instruction_values, instruction_text in occurrences[address]:
            classified_values = []
            for value in sorted(instruction_values):
                classified_values.append("0x%02X:%s" %
                                         (value, "+".join(sorted(instruction_values[value]))))
            output.write("scalar 0x%08X values=%s instruction=%s\n" %
                         (instruction_address,
                          ",".join(classified_values),
                          instruction_text))
        output.write(body.encode("ascii", "replace").decode("ascii"))

print("candidatos a produtor simples S->C: %d" % len(matches))
