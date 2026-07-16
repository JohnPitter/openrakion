# -*- coding: utf-8 -*-
# Localiza instrucoes que usam uma constante e decompila as funcoes proprietarias.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


DEFAULT_VALUE = 0x01910025
DEFAULT_OUTPUT = r"C:\temp\scalar_consumers.txt"


def parse_number(value):
    return int(value, 0)


args = getScriptArgs()
target = parse_number(args[0]) if args else DEFAULT_VALUE
output_path = args[1] if len(args) > 1 else DEFAULT_OUTPUT
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
monitor = ConsoleTaskMonitor()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
hits = []
functions = {}

instruction = listing.getInstructions(True)
while instruction.hasNext() and not monitor.isCancelled():
    current = instruction.next()
    matched = False
    for operand_index in range(current.getNumOperands()):
        for obj in current.getOpObjects(operand_index):
            if hasattr(obj, "getValue") and obj.getValue() == target:
                matched = True
                break
        if matched:
            break
    if not matched:
        continue
    function = manager.getFunctionContaining(current.getAddress())
    hits.append((current, function))
    if function is not None:
        functions[str(function.getEntryPoint())] = function

with open(output_path, "w") as output:
    output.write("value=0x%x hits=%d functions=%d\n" % (target, len(hits), len(functions)))
    for instruction, function in hits:
        owner = function.getName() if function is not None else "<sem funcao>"
        output.write("%s %s :: %s\n" % (instruction.getAddress(), owner, instruction))
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("consumidores de 0x%x extraidos em %s" % (target, output_path))
