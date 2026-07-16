# -*- coding: utf-8 -*-
# Extrai estado de Golden Sword e sincronização Golem em engine.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x36006FE0,  # FieldInfo::SetGoldSwordMode
    0x361095F0,  # CSessionState::GetMasterGolem
    0x361098E0,  # CSessionState::AddRemoteMasterGolem
    0x36109FE0,  # CSessionState::GetGoldGolem
    0x3610B1E0,  # CSessionState::SendInfoCreateMasterGolemTo
    0x3610C7C0,  # CSessionState::CreateMasterGolem
    0x3610C8C0,  # CSessionState::DestroyMasterGolems
    0x3610C960,  # CSessionState::SetMasterClient
    0x3610D060,  # CSessionState::BuildMapItemList
    0x3610D6A0,  # CSessionState::SendInfoMapItemStatus
    0x3610D730,  # CSessionState::HandleMessage
)
OUTPUT = r"C:\temp\engine_golem_objective.txt"

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
reference_lines = []

for offset in TARGETS:
    function = manager.getFunctionAt(toAddr(offset))
    if function is None:
        function = manager.getFunctionContaining(toAddr(offset))
    if function is None:
        continue
    functions[str(function.getEntryPoint())] = function
    for reference in references.getReferencesTo(function.getEntryPoint()):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        reference_lines.append(
            "%s <- %s em %s @ %s\n" % (
                function.getEntryPoint(),
                reference.getFromAddress(),
                caller.getName() if caller else "(sem funcao)",
                caller.getEntryPoint() if caller else "-",
            )
        )
        if caller is not None:
            functions[str(caller.getEntryPoint())] = caller

with open(OUTPUT, "w") as output:
    output.write("===== referencias =====\n")
    for line in reference_lines:
        output.write(line)
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("objetivos Golem do engine extraidos em " + OUTPUT)
