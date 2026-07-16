# -*- coding: utf-8 -*-
# Audita se o dispatcher World S->C do engine.dll aceita respostas da loteria.
# Saida: C:\temp\client_world_lottery_support.txt
# @category Rakion
import re

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


DISPATCHER = 0x36197320
LOTTERY_OPCODES = (0x75, 0x76)
OUTPUT = r"C:\temp\client_world_lottery_support.txt"

manager = currentProgram.getFunctionManager()
function = manager.getFunctionAt(toAddr(DISPATCHER))
if function is None:
    raise ValueError("dispatcher ausente em 0x%08X" % DISPATCHER)

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
result = decompiler.decompileFunction(function, 240, ConsoleTaskMonitor())
if not result or not result.getDecompiledFunction():
    raise ValueError("falha ao decompilar dispatcher")

code = result.getDecompiledFunction().getC()
cases = sorted(set(int(value, 0) for value in re.findall(
    r"case\s+(0x[0-9a-fA-F]+|[0-9]+):", code)))
present = [opcode for opcode in LOTTERY_OPCODES if opcode in cases]

with open(OUTPUT, "w") as output:
    output.write("program=%s\n" % currentProgram.getName())
    output.write("dispatcher=0x%08X cases=%d max=0x%02X\n" %
                 (DISPATCHER, len(cases), max(cases)))
    output.write("lottery_0x75=%s\n" % ("present" if 0x75 in cases else "absent"))
    output.write("lottery_0x76=%s\n" % ("present" if 0x76 in cases else "absent"))

if present:
    raise ValueError("opcodes de loteria inesperadamente presentes: %s" % present)

print("cliente World S->C sem 0x75/0x76; evidencia em %s" % OUTPUT)
