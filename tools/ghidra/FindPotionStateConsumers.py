# -*- coding: utf-8 -*-
# Localiza consumidores dos subobjetos/propriedades de Steam e Scouter.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


OFFSETS = (
    0x2C40, 0x2C44,  # Steam: subobjeto de estado e propriedade interna
    0x2C4C, 0x2C50,  # Steam: subobjeto de timestamp e propriedade interna
    0x2C58, 0x2C5C,  # Scouter: subobjeto de estado e propriedade interna
    0x2C64, 0x2C68,  # Scouter: subobjeto de timestamp e propriedade interna
)
FUNCTION_STARTS = (0x3514A8B0,)  # CPlayer::StartRound
OUTPUT = r"C:\temp\potion_state_consumers.txt"

listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
monitor = ConsoleTaskMonitor()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
hits = []
functions = {}

for offset in FUNCTION_STARTS:
    address = toAddr(offset)
    function = manager.getFunctionAt(address)
    if function is None:
        disassemble(address)
        function = createFunction(address, None)

instructions = listing.getInstructions(True)
while instructions.hasNext() and not monitor.isCancelled():
    instruction = instructions.next()
    matched = set()
    rendered = str(instruction).lower()
    for value in OFFSETS:
        if "0x%x" % value in rendered:
            matched.add(value)
    for operand_index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(operand_index):
            if hasattr(obj, "getValue"):
                value = obj.getValue()
                if value in OFFSETS:
                    matched.add(value)
    if not matched:
        continue
    function = manager.getFunctionContaining(instruction.getAddress())
    hits.append((instruction, function, sorted(matched)))
    if function is not None:
        functions[str(function.getEntryPoint())] = function

with open(OUTPUT, "w") as output:
    output.write("offsets=%s hits=%d functions=%d\n" % (
        ",".join("0x%x" % value for value in OFFSETS), len(hits), len(functions)))
    for instruction, function, matched in hits:
        owner = function.getName() if function is not None else "<sem funcao>"
        output.write("%s [%s] %s :: %s\n" % (
            instruction.getAddress(), ",".join("0x%x" % value for value in matched),
            owner, instruction))
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("consumidores do estado de potion extraidos em " + OUTPUT)
