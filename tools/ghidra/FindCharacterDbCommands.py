# Busca as rotinas de banco do ciclo de personagem por strings SQL/log e decompila os consumidores.
# Saida: C:\temp\character_db_commands.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

needles = [
    "DBCommandCharacterCreate",
    "INSERT INTO CharacterInfo(name,userid,class,slot,createtime,changetime)",
    "DBCommandCharacterStateClear",
    "LogCharStateClear",
    "DBCommandCharacterChangeCharName",
    "LogChangeCharName",
]

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
callers = {}
hits = []

for data in currentProgram.getListing().getDefinedData(True):
    value = data.getValue()
    if value is None:
        continue
    text = str(value)
    if not any(needle.lower() in text.lower() for needle in needles):
        continue
    hits.append((data.getAddress(), text))
    for ref in rm.getReferencesTo(data.getAddress()):
        function = fm.getFunctionContaining(ref.getFromAddress())
        if function:
            functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\character_db_commands.txt", "w") as output:
    output.write("=== strings encontradas ===\n")
    for address, value in hits:
        output.write("%s %s\n" % (address, value))
    output.write("\n=== funcoes consumidoras ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decomp.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))
        for ref in rm.getReferencesTo(function.getEntryPoint()):
            caller = fm.getFunctionContaining(ref.getFromAddress())
            if caller:
                callers[caller.getEntryPoint().getOffset()] = caller

    output.write("\n=== callers das rotinas ===\n")
    for entry in sorted(callers):
        function = callers[entry]
        result = decomp.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("character db: %d strings, %d funcoes" % (len(hits), len(functions)))
