# -*- coding: utf-8 -*-
# Localiza os chamadores do envio ao lobby que tratam o retorno interno 0x17.
# Saida: C:\temp\world_power_user_client_callback.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

SEND_LOBBY = 0x004038E0

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
send_function = fm.getFunctionAt(toAddr(SEND_LOBBY))
functions = {}

for reference in rm.getReferencesTo(send_function.getEntryPoint()):
    caller = fm.getFunctionContaining(reference.getFromAddress())
    if caller:
        functions[caller.getEntryPoint().getOffset()] = caller

matches = []
for entry in sorted(functions):
    function = functions[entry]
    result = decompiler.decompileFunction(function, 180, monitor)
    if not result or not result.getDecompiledFunction():
        continue
    code = result.getDecompiledFunction().getC()
    lowered = code.lower()
    if "0x34" in lowered or "0x17" in lowered or "power" in lowered:
        matches.append((function, code))

with open(r"C:\temp\world_power_user_client_callback.txt", "w") as output:
    output.write("send lobby: %s\n" % send_function.getName(True))
    output.write("callers: %d, matches: %d\n" % (len(functions), len(matches)))
    for function, code in matches:
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("World callback PU: %d chamadores, %d candidatos" %
      (len(functions), len(matches)))
