# -*- coding: utf-8 -*-
# Extrai bindings de CP, inicialização do Player e snapshot de late join da build v258.
# @category Rakion
import re

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS_BY_PROGRAM = {
    "rakion_orig.exe": (
        0x0045FDD0,  # registra CPlayer/GetCP/AddCP/ReduceCP/GetMaxCP no runtime Lua
    ),
    "entitiesmp_dump.bin": (
        0x35130AF0,  # GetMaxCP
        0x35130B40,  # SetMaxCP
        0x35130B60,  # GetCP
        0x35135760,  # SetCP
        0x351357D0,  # AddCP
        0x351357F0,  # ReduceCP
        0x35141000,  # GetInitData
        0x35158DB0,  # CPlayer constructor
        0x3515A0E0,  # ApplyInitData
        0x35162370,  # Respawn
    ),
    "entitiesmp.dll": (
        0x35130AF0,
        0x35130B40,
        0x35130B60,
        0x35135760,
        0x351357D0,
        0x351357F0,
        0x35141000,
        0x35158DB0,
        0x3515A0E0,
        0x35162370,
    ),
    "engine.dll": (
        0x36109BE0,  # SendInfoCreateNpcTo
        0x36109CC0,  # SendInfoCreateMapNpcTo
        0x3610B1E0,  # SendInfoCreateMasterGolemTo
        0x3610D6A0,  # SendInfoMapItemStatus
        0x3610E2B0,  # CSessionState::AddRemotePlayer
    ),
}


program_name = currentProgram.getName().lower()
targets = TARGETS_BY_PROGRAM.get(program_name)
if targets is None:
    raise ValueError("programa sem catálogo de Cells: %s" % currentProgram.getName())

args = getScriptArgs()
safe_name = re.sub(r"[^A-Za-z0-9_.-]", "_", program_name)
output_path = args[0] if args else r"C:\temp\client_cell_runtime_%s.txt" % safe_name
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
memory = currentProgram.getMemory()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(output_path, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    for address in targets:
        target = toAddr(address)
        function = manager.getFunctionContaining(target)
        if function is None and memory.contains(target):
            disassemble(target)
            createFunction(target, None)
            function = manager.getFunctionContaining(target)
        if function is None:
            output.write("\n===== 0x%08x ausente =====\n" % address)
            continue
        callers = []
        for reference in references.getReferencesTo(function.getEntryPoint()):
            caller = manager.getFunctionContaining(reference.getFromAddress())
            if caller is not None:
                callers.append("%s@%s" % (caller.getName(), caller.getEntryPoint()))
        output.write("\n===== %s @ %s callers=%s =====\n" % (
            function.getName(), function.getEntryPoint(), ",".join(callers)))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())
        output.write("\n")

print("runtime de Cells extraido em %s" % output_path)
