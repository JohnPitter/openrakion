# -*- coding: utf-8 -*-
# Extrai o handshake TCP 0x0E SuccessUDP nas três pontas.
# Saidas: C:\temp\world_success_udp.txt, C:\temp\engine_success_udp.txt e
#         C:\temp\rakion_success_udp.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = {
    "worldserv.exe": (
        (0x0041FA40, "handler C->S 0x0E SuccessUDP"),
        (0x0040ABE0, "projeta os dois endpoints da sessao"),
    ),
    "engine.dll": (
        (0x36190C20, "builder C->S 0x0E SuccessUDP"),
        (0x36192DA0, "parser S->C 0x0E SuccessUDP"),
    ),
    "rakion.bin": (
        (0x00477200, "callback UI S->C 0x0E SuccessUDP"),
    ),
}

OUTPUTS = {
    "worldserv.exe": r"C:\temp\world_success_udp.txt",
    "engine.dll": r"C:\temp\engine_success_udp.txt",
    "rakion.bin": r"C:\temp\rakion_success_udp.txt",
}


def decompile(function, decompiler, monitor):
    result = decompiler.decompileFunction(function, 300, monitor)
    if result and result.decompileCompleted():
        return result.getDecompiledFunction().getC()
    return "(falha na decompilacao)"


def ascii_text(value):
    return value.encode("ascii", "replace").decode("ascii")


program_name = currentProgram.getName().lower()
if program_name not in TARGETS:
    raise RuntimeError("programa nao suportado: %s" % program_name)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
output_path = OUTPUTS[program_name]

with open(output_path, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    for address, purpose in TARGETS[program_name]:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            disassemble(toAddr(address))
            function = createFunction(toAddr(address), None)
        output.write("\n===== %s @ %08x - %s =====\n" % (
            function.getName() if function else "funcao ausente",
            address,
            purpose,
        ))
        if function:
            output.write(ascii_text(decompile(function, decompiler, monitor)))

print("SuccessUDP extraido: %s" % output_path)
