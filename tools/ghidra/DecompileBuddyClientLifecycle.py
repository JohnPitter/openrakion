# Reproduz o ciclo de vida do Messenger entre rakion.exe e Buddy2.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
functions = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
memory = currentProgram.getMemory()
monitor = ConsoleTaskMonitor()

targets = [
    (0x0040a8f0, "DestroyMessengerHost"),
    (0x0040b9b0, "GetMessengerHost"),
    (0x0040bf90, "CreateMessengerHost"),
    (0x004755b0, "LeaveWorldServer"),
    (0x0047bce0, "WorldLoginSuccess"),
    (0x0047cb40, "CharacterSelectResult"),
    (0x004785b0, "SetNicknameResult"),
    (0x00481e00, "MessengerHostConstructor"),
    (0x00483600, "RebuildMessengerWindow"),
    (0x00488a20, "SendBuddyLogin"),
    (0x0048a5d0, "BuddyLoginResult"),
    (0x0048c1d0, "BuddyCallbackHostConstructor"),
]


def address(offset):
    return space.getAddress(offset)


def decompile(offset, label):
    function = functions.getFunctionContaining(address(offset))
    if function is None:
        print("\n===== %s 0x%08x: ausente =====" % (label, offset))
        return
    result = decompiler.decompileFunction(function, 180, monitor)
    print("\n===== %s %s @ %s =====" % (
        label, function.getName(), function.getEntryPoint()))
    if result and result.decompileCompleted():
        print(result.getDecompiledFunction().getC())
    else:
        print("(falha na decompilacao)")


def print_callers(offset, label):
    seen = set()
    print("\n===== CALLERS %s 0x%08x =====" % (label, offset))
    for reference in references.getReferencesTo(address(offset)):
        function = functions.getFunctionContaining(reference.getFromAddress())
        if function is None:
            continue
        entry = function.getEntryPoint().getOffset()
        if entry in seen:
            continue
        seen.add(entry)
        print("%s @ %s via %s" % (
            function.getName(), function.getEntryPoint(), reference.getFromAddress()))


print("PROGRAM=%s" % currentProgram.getName())
for target, name in targets:
    decompile(target, name)

for target, name in targets:
    print_callers(target, name)

print("\n===== BUDDY CALLBACK VTABLE 0x004de7f0 =====")
for index in range(16):
    slot = address(0x004de7f0 + index * 4)
    value = memory.getInt(slot) & 0xffffffff
    function = functions.getFunctionContaining(address(value))
    print("slot[%02d] +0x%02x = 0x%08x %s" % (
        index, index * 4, value, function.getName() if function else "?"))

print("##### DONE #####")
