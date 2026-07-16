# -*- coding: utf-8 -*-
# Extrai consumidores de CNetworkLibrary::GetSessionProperties em entitiesmp.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
from jarray import array


GET_SESSION_PROPERTIES_IAT_RVA = 0x2B3364
OUTPUT = r"C:\temp\entitiesmp_session_properties.txt"

memory = currentProgram.getMemory()
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
target = memory.getMinAddress().add(GET_SESSION_PROPERTIES_IAT_RVA)
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
occurrences = 0

for reference in references.getReferencesTo(target):
    function = manager.getFunctionContaining(reference.getFromAddress())
    if function is not None:
        functions[str(function.getEntryPoint())] = function

pattern = array([0x64, 0x33, 0x2B, 0x35], "b")
cursor = memory.getMinAddress()
maximum = memory.getMaxAddress()
while cursor <= maximum:
    found = memory.findBytes(cursor, maximum, pattern, None, True, monitor)
    if found is None:
        break
    occurrences += 1
    function = manager.getFunctionContaining(found)
    if function is not None:
        functions[str(function.getEntryPoint())] = function
    cursor = found.add(1)

with open(OUTPUT, "w") as output:
    output.write("CNetworkLibrary::GetSessionProperties IAT @ %s\n" % target)
    output.write("operandos=%d\n" % occurrences)
    output.write("consumidores=%d\n" % len(functions))
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("consumidores de session properties extraidos em " + OUTPUT)
