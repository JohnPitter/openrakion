# Localiza as funcoes de CP/cell em entitiesmp.dll e decompila seus chamadores.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = (
    "GetCP",
    "AddCP",
    "ReduceCP",
    "GetMaxCP",
    "GetCellType",
    "GetNpcSetupData",
)

KNOWN_ENTITIESMP_ADDRESSES = (
    (0x350E9AE0, "ReleaseSummonSlot"),
    (0x350E9B30, "DisappearSummonedNpcWithRefund"),
    (0x350EE230, "NpcCombatDeath"),
    (0x35130AF0, "GetMaxCP"),
    (0x35130B60, "GetCP"),
    (0x35130B40, "SetMaxCP"),
    (0x35132D60, "GetCellName"),
    (0x351353A0, "UseCPPotion"),
    (0x35135760, "SetCP"),
    (0x351357D0, "AddCP"),
    (0x351357F0, "ReduceCP"),
    (0x3513F670, "EmitCPMessage"),
    (0x35144220, "CheckNpcSpawned"),
    (0x351DD710, "GetNpcSetupData"),
    (0x351DD800, "GetCellType"),
    (0x351DD540, "ReadNpcDataFromFile"),
    (0x351E5A70, "GetCreatureStr"),
    (0x351E6440, "ReadCreatureListFile"),
    (0x35228D10, "ReadNpcDataCore"),
    (0x3522B340, "ReadNpcFieldB340"),
    (0x3522B370, "ReadNpcFieldB370"),
    (0x3522B380, "ReadNpcFieldB380"),
    (0x3522B390, "ReadNpcFieldB390"),
    (0x3522B400, "ReadNpcFieldB400"),
)

decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()


def decompile(function, label):
    if function is None:
        print("\n===== %s: ausente =====" % label)
        return
    result = decomp.decompileFunction(function, 90, monitor)
    print("\n===== %s: %s @ %s =====" % (
        label,
        function.getName(),
        function.getEntryPoint(),
    ))
    if result and result.decompileCompleted():
        print(result.getDecompiledFunction().getC())
    else:
        print("(falha na decompilacao)")


def matches(name):
    return any(target.lower() in name.lower() for target in TARGETS)


print("PROGRAM=%s" % currentProgram.getName())
filters = tuple(value.lower() for value in getScriptArgs())
matched = []
matched_entries = set()
iterator = functions.getFunctions(True)
while iterator.hasNext():
    function = iterator.next()
    name = function.getName().lower()
    if ((filters and any(value in name for value in filters)) or
            (not filters and matches(function.getName()))):
        matched.append(function)
        matched_entries.add(function.getEntryPoint().getOffset())

address_space = currentProgram.getAddressFactory().getDefaultAddressSpace()
for offset, label in KNOWN_ENTITIESMP_ADDRESSES:
    if filters and not any(value in label.lower() for value in filters):
        continue
    address = address_space.getAddress(offset)
    function = functions.getFunctionAt(address)
    if function is None:
        function = functions.getFunctionContaining(address)
    block = currentProgram.getMemory().getBlock(address)
    if function is None and block is not None and block.isInitialized():
        disassemble(address)
        function = createFunction(address, "CPlayer_%s" % label)
    if function is not None and function.getEntryPoint().getOffset() not in matched_entries:
        matched.append(function)
        matched_entries.add(function.getEntryPoint().getOffset())

print("TARGET_COUNT=%d" % len(matched))
for target in matched:
    decompile(target, "TARGET")
    seen = set()
    callers = []
    for reference in references.getReferencesTo(target.getEntryPoint()):
        caller = functions.getFunctionContaining(reference.getFromAddress())
        if caller is None or caller == target:
            continue
        entry = caller.getEntryPoint().getOffset()
        if entry in seen:
            continue
        seen.add(entry)
        callers.append("%s @ %s via %s" % (
            caller.getName(),
            caller.getEntryPoint(),
            reference.getFromAddress(),
        ))
    print("CALLERS de %s (%d):" % (target.getName(), len(callers)))
    for caller in callers:
        print("  %s" % caller)

print("\n##### DONE #####")
