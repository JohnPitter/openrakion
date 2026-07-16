# Lista acessos diretos ao campo de CP e janelas das chamadas de CP conhecidas.
# @category Rakion

CP_FUNCTIONS = set((
    0x35130AF0,
    0x35130B40,
    0x35130B60,
    0x35135760,
    0x351357D0,
    0x351357F0,
))

listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()


def function_label(address):
    function = functions.getFunctionContaining(address)
    if function is None:
        return "sem_funcao"
    return "%s@%s" % (function.getName(), function.getEntryPoint())


def is_cp_call(instruction):
    for reference in instruction.getReferencesFrom():
        if reference.getToAddress().getOffset() in CP_FUNCTIONS:
            return True
    return False


print("PROGRAM=%s" % currentProgram.getName())
iterator = listing.getInstructions(True)
matches = []
while iterator.hasNext():
    instruction = iterator.next()
    rendered = instruction.toString().lower()
    direct_field = "2714" in rendered or "0xb8c" in rendered
    if direct_field or is_cp_call(instruction):
        matches.append(instruction)

print("MATCH_COUNT=%d" % len(matches))
for instruction in matches:
    print("%s | %s | %s" % (
        instruction.getAddress(),
        function_label(instruction.getAddress()),
        instruction,
    ))

address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(0x352B49CC)
try:
    bits = currentProgram.getMemory().getInt(address)
    print("DEATH_CP_FACTOR_BITS=0x%08x" % (bits & 0xffffffff))
except:
    print("DEATH_CP_FACTOR_BITS=indisponivel")

for pointer_address in (0x352B32D8, 0x352B3030, 0x352B303C):
    address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(pointer_address)
    try:
        value = currentProgram.getMemory().getInt(address) & 0xffffffff
        print("POINTER_0x%08x=0x%08x" % (pointer_address, value))
    except:
        print("POINTER_0x%08x=indisponivel" % pointer_address)

print("##### DONE #####")
