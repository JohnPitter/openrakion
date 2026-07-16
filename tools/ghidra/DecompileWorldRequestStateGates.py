# -*- coding: utf-8 -*-
# Extrai handlers interceptados pelo .NET para auditar gates de fase/identidade do World v258.
# Saida: C:\temp\world_request_state_gates.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    (0x0E, 0x0041FA40), (0x0F, 0x0041FB30),
    (0x12, 0x0041FCD0), (0x13, 0x0041FE10), (0x14, 0x0041FEF0),
    (0x15, 0x00420120), (0x19, 0x00420760), (0x1A, 0x00420840),
    (0x1B, 0x004208E0), (0x1C, 0x00420A40),
    (0x28, 0x0041DE40),
    (0x2C, 0x00420DE0), (0x2D, 0x00420F10), (0x2E, 0x00421210),
    (0x2F, 0x004215A0), (0x31, 0x00421870), (0x32, 0x004226B0),
    (0x34, 0x00422B10), (0x35, 0x00422850), (0x36, 0x00422C90),
    (0x38, 0x00423100), (0x39, 0x00423300), (0x3A, 0x004234E0),
    (0x3B, 0x00423580), (0x48, 0x00424640), (0x4A, 0x004246E0),
    (0x4B, 0x004247B0), (0x4F, 0x00424A20), (0x53, 0x00425010),
    (0x6B, 0x004286A0), (0x6C, 0x00428750), (0x6D, 0x00428A10),
    (0x6F, 0x00428D80), (0x70, 0x004292B0), (0x71, 0x004293F0),
    (0x73, 0x00421A50),
)
HELPERS = (
    ("OPCODE 0x34 policy", 0x0040B2C0),
)
OUTPUT = r"C:\temp\world_request_state_gates.txt"

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(OUTPUT, "w") as output:
    for opcode, address in TARGETS:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            raise ValueError("funcao ausente para 0x%02X em 0x%08X" % (opcode, address))
        result = decompiler.decompileFunction(function, 300, monitor)
        if not result or not result.getDecompiledFunction():
            raise ValueError("falha ao decompilar opcode 0x%02X" % opcode)
        output.write("\n===== OPCODE 0x%02X | %s @ %s =====\n" %
                     (opcode, function.getName(True), function.getEntryPoint()))
        output.write(result.getDecompiledFunction().getC())
        output.write("\n")
    for label, address in HELPERS:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            raise ValueError("helper ausente em 0x%08X" % address)
        result = decompiler.decompileFunction(function, 300, monitor)
        if not result or not result.getDecompiledFunction():
            raise ValueError("falha ao decompilar " + label)
        output.write("\n===== %s | %s @ %s =====\n" %
                     (label, function.getName(True), function.getEntryPoint()))
        output.write(result.getDecompiledFunction().getC())
        output.write("\n")

print("gates de requests extraidos para " + OUTPUT)
