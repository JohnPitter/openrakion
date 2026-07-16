# -*- coding: utf-8 -*-
# Cruza o catalogo engine.dll com a vtable de callbacks World do rakion.bin.
# Entrada: C:\temp\client_world_response_catalog.tsv
# Saidas: C:\temp\rakion_world_response_callbacks.txt/.tsv
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


CATALOG = r"C:\temp\client_world_response_catalog.tsv"
OUTPUT = r"C:\temp\rakion_world_response_callbacks.txt"
TSV_OUTPUT = r"C:\temp\rakion_world_response_callbacks.tsv"
VTABLE = 0x004DDC08

functions = currentProgram.getFunctionManager()
memory = currentProgram.getMemory()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
rows = []

with open(CATALOG, "r") as source:
    for line in source.readlines()[1:]:
        opcode_text, handler, destination = line.strip().split("\t")
        if not destination.startswith("callback+"):
            rows.append((int(opcode_text, 0), handler, destination, None, None))
            continue
        slot = int(destination.split("+", 1)[1], 0)
        pointer = memory.getInt(toAddr(VTABLE + slot)) & 0xffffffff
        function = functions.getFunctionAt(toAddr(pointer))
        if function is None and 0x00400000 <= pointer < 0x00500000:
            disassemble(toAddr(pointer))
            function = createFunction(toAddr(pointer), None)
        if function is None:
            raise ValueError("callback 0x%02X ausente em 0x%08X" %
                             (int(opcode_text, 0), pointer))
        rows.append((int(opcode_text, 0), handler, destination, pointer, function))

if len(rows) != 88:
    raise ValueError("esperadas 88 rows, obtidas %d" % len(rows))

with open(TSV_OUTPUT, "w") as output:
    output.write("opcode\tdestino\timplementacao\n")
    for opcode, handler, destination, pointer, function in rows:
        implementation = "envia-0x61" if function is None else "0x%08X" % pointer
        output.write("0x%02X\t%s\t%s\n" %
                     (opcode, destination, implementation))

with open(OUTPUT, "w") as output:
    output.write("vtable=0x%08X rows=%d\n" % (VTABLE, len(rows)))
    for opcode, handler, destination, pointer, function in rows:
        if function is None:
            output.write("\n===== opcode=0x%02X %s %s =====\n" %
                         (opcode, handler, destination))
            continue
        output.write("\n===== opcode=0x%02X %s %s implementation=0x%08X %s =====\n" %
                     (opcode, handler, destination, pointer, function.getName(True)))
        result = decompiler.decompileFunction(function, 180, monitor)
        body = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write(body.encode("ascii", "replace").decode("ascii"))

print("callbacks World finais do rakion.bin: %d" % len(rows))
