# -*- coding: utf-8 -*-
# Extrai as filas request/response do worker DB do World v258.
# Saida: C:\temp\world_db_queues.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x0041B940,  # enqueue World -> worker DB
    0x0041B3F0,  # thread que drena requests
    0x0041AE50,  # dispatcher de comandos DB
    0x004138B0,  # comando 0x0C: persiste EXP
    0x004121E0,  # comando 0x05: persiste o IPv4 anunciado no handshake UDP1
    0x00412140,  # comando 0x04: encerra o registro LogUserConnect
    0x004107D0,  # comando 0x02: login e criação do registro de conexão
    0x0041BDE0,  # opcode 0x78 -> comando DB 0x2C
    0x0040F610,  # comando DB 0x2C: consulta membros do cla
    0x0042BD70,  # loop World que drena respostas DB
    0x004295C0,  # dispatcher de callbacks DB
    0x0041E1A0,  # callback DB 0x2C -> S->C 0x78
    0x00424350,  # saída: DB 0x0C + S->C 0x58
)
OUTPUT = r"C:\temp\world_db_queues.txt"

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for address in TARGETS:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            raise ValueError("funcao ausente em 0x%08X" % address)
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 300, monitor)
        if not result or not result.getDecompiledFunction():
            raise ValueError("falha ao decompilar 0x%08X" % address)
        output.write(result.getDecompiledFunction().getC())
        output.write("\n")

print("filas DB do World extraidas para " + OUTPUT)
