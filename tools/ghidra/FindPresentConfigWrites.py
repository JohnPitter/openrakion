# Localiza funcoes que referenciam os campos de configuracao de random present no CWorld.
# Saida: C:\temp\present_config_writes.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

OFFSETS = set([0x51FC, 0x5200, 0x5204, 0x5208, 0x520C, 0x5210, 0x5214,
               0x5218, 0x521C, 0x5220, 0x5224, 0x5228, 0x522C])
listing = currentProgram.getListing()
fm = currentProgram.getFunctionManager()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
hits = []

for instruction in listing.getInstructions(True):
    matched = []
    for index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(index):
            if isinstance(obj, Scalar) and (obj.getUnsignedValue() & 0xffffffff) in OFFSETS:
                matched.append(obj.getUnsignedValue() & 0xffffffff)
    if not matched:
        continue
    function = fm.getFunctionContaining(instruction.getAddress())
    hits.append((instruction.getAddress(), str(instruction), matched))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\present_config_writes.txt", "w") as output:
    output.write("=== instrucoes ===\n")
    for address, instruction, matched in hits:
        output.write("%s %s offsets=%s\n" % (address, instruction, matched))
    output.write("\n=== funcoes ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decomp.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("present config: %d instrucoes, %d funcoes" % (len(hits), len(functions)))
