# -*- coding: utf-8 -*-
# Extrai o job dedicado RankUpdate: personagens, classes, clas e rotacao de snapshots.
# Saida: C:\temp\rank_update.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

KEYWORDS = [
    "insert into totalrank", "update totalrank set grade", "select class,username",
    "insert into swordmanrank", "insert into clanrank", "update low_priority characterinfo",
    "update low_priority usergameinfo", "alter table totalrank rename", "alter table clanrank rename"
]
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
    if not any(keyword in value.lower() for keyword in KEYWORDS):
        continue
    strings.append((data.getAddress(), value))
    for reference in references.getReferencesTo(data.getAddress()):
        function = manager.getFunctionContaining(reference.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\rank_update.txt", "w") as output:
    output.write("=== strings ===\n")
    for address, value in strings:
        output.write("%s %s\n" %
                     (address, value.encode("ascii", "replace").decode("ascii")))
    output.write("\n=== consumidores ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("rank update: %d strings, %d funcoes" % (len(strings), len(functions)))
