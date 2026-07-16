# -*- coding: utf-8 -*-
# Localiza quem le/grava os limites por modo usados por FUN_0041CF80.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

OFFSETS = set((0x52B8, 0x52BC, 0x52C0, 0x52C4, 0x52C8, 0x52CC, 0x52D0, 0x52D4, 0x52D8, 0x52DC))
listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
hits = {}

for instruction in listing.getInstructions(True):
    matched = set()
    for operand_index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(operand_index):
            if isinstance(obj, Scalar) and obj.getUnsignedValue() in OFFSETS:
                matched.add(obj.getUnsignedValue())
    if not matched:
        continue
    function = manager.getFunctionContaining(instruction.getAddress())
    if function is not None:
        entry = function.getEntryPoint().getOffset()
        hits.setdefault(entry, set()).update(matched)

decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
with open(r"C:\temp\world_game_point_config.txt", "w") as output:
    for entry in sorted(hits):
        function = manager.getFunctionAt(toAddr(entry))
        output.write("\n===== %s @ %08x offsets=%s =====\n" % (
            function.getName(), entry, ",".join("%x" % value for value in sorted(hits[entry]))))
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("configuracao de game points localizada: %d funcoes" % len(hits))
