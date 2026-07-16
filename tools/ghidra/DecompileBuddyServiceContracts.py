# Decompila todos os builders/consumidores dos comandos de amizade e grupos do Buddy2.dll.
# Uso: analyzeHeadless <project-dir> <project> -process Buddy2.dll -noanalysis
#      -scriptPath <repo>\tools\ghidra -postScript DecompileBuddyServiceContracts.py
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

COMMANDS = [
    0x2020, 0x2021,
    0x3000, 0x3001, 0x3002, 0x3003, 0x3004, 0x3005, 0x3006, 0x3007,
    0x3100, 0x3101, 0x3102, 0x3103, 0x3104, 0x3105, 0x3110, 0x3111,
    0x3150, 0x3151, 0x3152, 0x3153, 0x3154, 0x3155, 0x3156, 0x3157,
    0x3FFF,
]

program = currentProgram
listing = program.getListing()
functions = program.getFunctionManager()
monitor = ConsoleTaskMonitor()
decompiler = DecompInterface()
decompiler.openProgram(program)


def referenced_commands(instruction):
    text = instruction.toString().lower()
    return [command for command in COMMANDS if ("0x%x" % command) in text]


hits = {}
for instruction in listing.getInstructions(True):
    commands = referenced_commands(instruction)
    if not commands:
        continue
    function = functions.getFunctionContaining(instruction.getAddress())
    if function is None:
        continue
    entry = function.getEntryPoint().getOffset()
    record = hits.setdefault(entry, {"function": function, "commands": set(), "refs": []})
    record["commands"].update(commands)
    record["refs"].append((instruction.getAddress(), instruction.toString()))

for entry in sorted(hits):
    record = hits[entry]
    function = record["function"]
    print("\n===== %s @ %s commands=%s =====" % (
        function.getName(), function.getEntryPoint(),
        ",".join("0x%04X" % value for value in sorted(record["commands"]))))
    for address, text in record["refs"]:
        print("REF %s %s" % (address, text))
    result = decompiler.decompileFunction(function, 180, monitor)
    if result and result.decompileCompleted():
        print(result.getDecompiledFunction().getC())
    else:
        print("DECOMPILE_FAILED")

print("\nSUMMARY functions=%d commands=%d" % (
    len(hits), len(set(command for record in hits.values() for command in record["commands"]))))
