# -*- coding: utf-8 -*-
# Localiza montagem SVC_SMS_SEND 0x2030 e consumo RET_SMS_SEND 0x2031 no Buddy2 original.
# Saida: C:\temp\buddy_sms_flows.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
strings = []

for data in listing.getDefinedData(True):
    value = str(data.getValue())
    if "sms" not in value.lower():
        continue
    strings.append((data.getAddress(), value))
    for reference in references.getReferencesTo(data.getAddress()):
        function = manager.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

for instruction in listing.getInstructions(True):
    for index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(index):
            if isinstance(obj, Scalar) and obj.getUnsignedValue() in (0x2030, 0x2031):
                function = manager.getFunctionContaining(instruction.getAddress())
                if function:
                    functions[function.getEntryPoint().getOffset()] = function

for function in list(functions.values()):
    for reference in references.getReferencesTo(function.getEntryPoint()):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\buddy_sms_flows.txt", "w") as output:
    output.write("=== strings ===\n")
    for address, value in strings:
        output.write("%s %s\n" % (address, value.encode("ascii", "replace").decode("ascii")))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("buddy sms: %d strings, %d funcoes" % (len(strings), len(functions)))
