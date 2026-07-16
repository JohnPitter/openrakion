# -*- coding: utf-8 -*-
# Extrai a UI produtora do request de criacao de sala 0x3B no rakion.bin.
# Saida: C:\temp\client_room_create.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x00448F40,  # indice do preset de faixa de level
    0x00448F80,  # indice do modo selecionado
    0x00449440,  # inputs de rounds/duracao/frag
    0x00449780,  # botoes e nomes dos modos
    0x00449C30,  # construtor dos presets de faixa de level
    0x0044AC70,  # evento Create e chamada virtual +0xDC
)
CALL_START = 0x0044B840
CALL_END = 0x0044B8D5
OUTPUT = r"C:\temp\client_room_create.txt"

manager = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    output.write("program=%s imageBase=%s\n" % (
        currentProgram.getName(), currentProgram.getImageBase()))
    output.write("\n===== pushes do SendFieldCreate =====\n")
    instruction = listing.getInstructionAt(toAddr(CALL_START))
    if instruction is None:
        instruction = listing.getInstructionAfter(toAddr(CALL_START))
    while instruction and instruction.getAddress().getOffset() <= CALL_END:
        output.write("%s %s\n" % (instruction.getAddress(), instruction))
        instruction = instruction.getNext()

    for value in TARGETS:
        address = toAddr(value)
        function = manager.getFunctionContaining(address)
        if function is None:
            output.write("\n===== 0x%08X sem funcao =====\n" % value)
            continue
        output.write("\n===== %s @ %s =====\n" % (
            function.getName(True), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 300, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction()
                else "(falha de decompilacao)")
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("produtor 0x3B extraido em " + OUTPUT)
