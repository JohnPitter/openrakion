# -*- coding: utf-8 -*-
# Fecha o request C->S 0x1D e sua origem na resposta S->C de mesma opcode.
# Execute em engine.dll e rakion_orig.exe.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


PROGRAMS = {
    "engine.dll": (
        0x361911D0,  # SendChannelList(u8 firstId, u8 mode)
        0x361931E0,  # parser S->C 0x1D
    ),
    "rakion_orig.exe": (
        0x0040A3C0,  # preserva primeiro/ultimo ID da lista
        0x0046A0F0,  # dispatcher UI; evento 0x174
        0x00474260,  # callback da resposta S->C 0x1D
    ),
}

name = currentProgram.getName()
addresses = PROGRAMS.get(name)
if addresses is None:
    raise ValueError("programa nao suportado: %s" % name)

manager = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
monitor = ConsoleTaskMonitor()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
output_path = r"C:\temp\client_channel_list_request_%s.txt" % name.replace(".", "_")

with open(output_path, "w") as output:
    output.write("program=%s\n" % name)
    for address in addresses:
        function = manager.getFunctionContaining(toAddr(address))
        if function is None:
            output.write("\n===== ausente @ %08X =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 240, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

    if name == "rakion_orig.exe":
        output.write("\n===== argumentos do evento 0x174 =====\n")
        instruction = listing.getInstructionAt(toAddr(0x0046AA63))
        end = toAddr(0x0046AA7C)
        while instruction is not None and instruction.getAddress().compareTo(end) <= 0:
            output.write("%s  %s\n" % (instruction.getAddress(), instruction))
            instruction = instruction.getNext()

print("request ChannelList extraido em %s" % output_path)
