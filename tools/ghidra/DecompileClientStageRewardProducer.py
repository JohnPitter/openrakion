# -*- coding: utf-8 -*-
# Extrai o produtor nativo de reward de stage e o trecho que monta EXP, gold e Cell EXP.
# Saida: C:\temp\client_stage_reward_producer.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


FUNCTION_START = 0x3515C760
REWARD_START = 0x3515CAD0
REWARD_END = 0x3515CD90
OUTPUT = r"C:\temp\client_stage_reward_producer.txt"

manager = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

address = toAddr(FUNCTION_START)
function = manager.getFunctionAt(address)
if function is None:
    disassemble(address)
    function = createFunction(address, None)

with open(OUTPUT, "w") as output:
    output.write("program=%s imageBase=%s\n" % (
        currentProgram.getName(), currentProgram.getImageBase()))
    output.write("\n===== instrucoes do calculo e envio =====\n")
    instruction = listing.getInstructionAt(toAddr(REWARD_START))
    if instruction is None:
        instruction = listing.getInstructionAfter(toAddr(REWARD_START))
    while instruction and instruction.getAddress().getOffset() < REWARD_END:
        output.write("%s %s\n" % (instruction.getAddress(), instruction))
        instruction = instruction.getNext()

    if function is None:
        output.write("\n===== 0x%08X sem funcao =====\n" % FUNCTION_START)
    else:
        output.write("\n===== %s @ %s =====\n" % (
            function.getName(True), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 300, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction()
                else "(falha de decompilacao)")
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("produtor de reward de stage extraido em " + OUTPUT)
