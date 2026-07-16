# Decompila funcoes nos enderecos passados como argumentos hexadecimais.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = currentProgram.getFunctionManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()

print("PROGRAM=%s" % currentProgram.getName())
for raw in getScriptArgs():
    offset = int(raw, 0)
    address = space.getAddress(offset)
    function = functions.getFunctionContaining(address)
    if function is None:
        block = currentProgram.getMemory().getBlock(address)
        if block is not None and block.isInitialized():
            disassemble(address)
            function = createFunction(address, "FUN_%08x" % offset)
    if function is None:
        print("\n===== 0x%08x: ausente =====" % offset)
        continue
    result = decomp.decompileFunction(function, 90, monitor)
    print("\n===== %s @ %s =====" % (function.getName(), function.getEntryPoint()))
    if result and result.decompileCompleted():
        print(result.getDecompiledFunction().getC())
    else:
        print("(falha na decompilacao)")

print("##### DONE #####")
