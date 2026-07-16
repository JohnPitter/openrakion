# -*- coding: utf-8 -*-
# Localiza registros e consumidores de respawn/invulnerabilidade em gamemp.dll desembrulhado.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGET_RVAS = (
    0x29DC8,  # persistent user FLOAT gam_tmSpawnInvulnerability;
    0x29E20,  # persistent user INDEX gam_bRespawnInPlace;
    0x2B28C,  # \respawninplace\%d
)
GLOBAL_RVAS = (0x36228, 0x36248)
FUNCTION_RVAS = (0x13AE0, 0x1D0D0)
OUTPUT = r"C:\temp\gamemp_respawn_settings.txt"

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
base = currentProgram.getMemory().getMinAddress()

with open(OUTPUT, "w") as output:
    for target_rva in TARGET_RVAS:
        address = base.add(target_rva)
        output.write("===== STRING @ %s =====\n" % address)
        for reference in references.getReferencesTo(address):
            caller = manager.getFunctionContaining(reference.getFromAddress())
            output.write("%s <- %s @ %s\n" % (
                reference.getFromAddress(),
                caller.getName() if caller else "(sem funcao)",
                caller.getEntryPoint() if caller else "-"))
            if caller:
                functions[str(caller.getEntryPoint())] = caller

    for global_rva in GLOBAL_RVAS:
        address = base.add(global_rva)
        output.write("===== GLOBAL @ %s =====\n" % address)
        for reference in references.getReferencesTo(address):
            caller = manager.getFunctionContaining(reference.getFromAddress())
            output.write("%s <- %s @ %s\n" % (
                reference.getFromAddress(),
                caller.getName() if caller else "(sem funcao)",
                caller.getEntryPoint() if caller else "-"))
            if caller:
                functions[str(caller.getEntryPoint())] = caller

    for function_rva in FUNCTION_RVAS:
        address = base.add(function_rva)
        function = manager.getFunctionAt(address)
        if function is None:
            disassemble(address)
            function = createFunction(address, None)
        if function:
            functions[str(function.getEntryPoint())] = function

    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())
        for reference in references.getReferencesTo(function.getEntryPoint()):
            caller = manager.getFunctionContaining(reference.getFromAddress())
            output.write("\nCALLER %s %s\n" % (
                reference.getFromAddress(), caller.getName() if caller else "(sem funcao)"))

print("settings de respawn extraidos em " + OUTPUT)
