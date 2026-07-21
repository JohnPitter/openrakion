# Dump the exact instructions around the client-side bot hit candidate sites.

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


OUTPUT = r"C:\temp\client_bot_hit_sites.txt"
RANGES = (
    (0x35152DA0, 0x35152E30, "ReceiveDamage entry"),
    (0x35153380, 0x35153430, "ReceiveDamage post-validation"),
    (0x3517D3D0, 0x3517D550, "UpdateWeaponHit"),
    (0x35153C80, 0x35153D40, "AddHitCount"),
    (0x35153CE0, 0x35153D80, "AddHitCount entry"),
    (0x3518CDF0, 0x3518CF80, "Previously attributed remote damage site"),
)


listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for start, end, label in RANGES:
        output.write("\n===== %s =====\n" % label)
        instruction = listing.getInstructionContaining(toAddr(start))
        if instruction is None:
            disassemble(toAddr(start))
            instruction = listing.getInstructionContaining(toAddr(start))
        while instruction is not None and instruction.getAddress().getOffset() < end:
            bytes_text = " ".join("%02x" % (value & 0xff) for value in instruction.getBytes())
            output.write("%s  %-24s  %s\n" % (
                instruction.getAddress(), bytes_text, instruction.toString()))
            instruction = instruction.getNext()

    output.write("\n===== ReceiveDamage callers =====\n")
    references = currentProgram.getReferenceManager().getReferencesTo(toAddr(0x35152DA0))
    for reference in references:
        call = listing.getInstructionContaining(reference.getFromAddress())
        if call is None:
            continue
        instruction = call
        for _ in range(12):
            previous = instruction.getPrevious()
            if previous is None:
                break
            instruction = previous
        output.write("\n--- caller at %s ---\n" % reference.getFromAddress())
        for _ in range(25):
            if instruction is None:
                break
            bytes_text = " ".join("%02x" % (value & 0xff) for value in instruction.getBytes())
            output.write("%s  %-24s  %s\n" % (
                instruction.getAddress(), bytes_text, instruction.toString()))
            instruction = instruction.getNext()

    output.write("\n===== Previously attributed site decompile =====\n")
    function_manager = currentProgram.getFunctionManager()
    function = function_manager.getFunctionContaining(toAddr(0x3518CE40))
    if function is not None:
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC().encode("ascii", "replace").decode("ascii"))
        else:
            output.write("(falha na decompilacao)")
    else:
        output.write("(funcao nao identificada)")
        previous = None
        following = None
        for candidate in function_manager.getFunctions(True):
            offset = candidate.getEntryPoint().getOffset()
            if offset < 0x3518CE40:
                previous = candidate
            elif offset > 0x3518CE40:
                following = candidate
                break
        output.write("\nprevious=%s\nfollowing=%s\n" % (previous, following))
        if previous is not None:
            result = decompiler.decompileFunction(previous, 240, monitor)
            if result and result.getDecompiledFunction():
                output.write(result.getDecompiledFunction().getC().encode("ascii", "replace").decode("ascii"))

    output.write("\n===== Player +0x394 references =====\n")
    for instruction in listing.getInstructions(True):
        offset = instruction.getAddress().getOffset()
        if offset < 0x35100000 or offset >= 0x35200000:
            continue
        text = instruction.toString().lower()
        if "0x394" not in text:
            continue
        owner = function_manager.getFunctionContaining(instruction.getAddress())
        output.write("%s  %-40s  function=%s\n" % (
            instruction.getAddress(), instruction.toString(), owner))

    output.write("\n===== Player +0x394 state transition decompile =====\n")
    gate = function_manager.getFunctionAt(toAddr(0x3518C380))
    if gate is not None:
        result = decompiler.decompileFunction(gate, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC().encode("ascii", "replace").decode("ascii"))

print("sites de hit do cliente extraidos em " + OUTPUT)
