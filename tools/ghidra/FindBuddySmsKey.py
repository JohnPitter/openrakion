# -*- coding: utf-8 -*-
# Localiza leituras/escritas do contexto de cifra de SMS do Buddy2.dll.
# Saida: C:\temp\buddy_sms_key.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.scalar import Scalar
from ghidra.util.task import ConsoleTaskMonitor

TARGET_OFFSETS = (0x13B18, 0x13B14, 0x13B1C)
KEY_DERIVATION = toAddr(0x1000ADA0)
STATIC_CREDENTIAL_CONTEXT = toAddr(0x10021430)
AES_SETUP = toAddr(0x1000B490)

listing = currentProgram.getListing()
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
matches = []
functions = {}
credential_functions = {}
static_context_functions = {}
aes_setup_callers = {}

for instruction in listing.getInstructions(True):
    values = []
    for index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(index):
            if isinstance(obj, Scalar):
                values.append(obj.getUnsignedValue())
    if not any(value in TARGET_OFFSETS for value in values):
        continue
    function = manager.getFunctionContaining(instruction.getAddress())
    if function:
        functions[function.getEntryPoint().getOffset()] = function
    matches.append((instruction.getAddress(), function, str(instruction)))

for instruction in listing.getInstructions(True):
    found = False
    for index in range(instruction.getNumOperands()):
        for obj in instruction.getOpObjects(index):
            if isinstance(obj, Scalar) and obj.getUnsignedValue() == 0x1000:
                found = True
    if found:
        function = manager.getFunctionContaining(instruction.getAddress())
        if function:
            credential_functions[function.getEntryPoint().getOffset()] = function

for reference in currentProgram.getReferenceManager().getReferencesTo(KEY_DERIVATION):
    function = manager.getFunctionContaining(reference.getFromAddress())
    if function:
        credential_functions[function.getEntryPoint().getOffset()] = function

for reference in currentProgram.getReferenceManager().getReferencesTo(STATIC_CREDENTIAL_CONTEXT):
    function = manager.getFunctionContaining(reference.getFromAddress())
    if function:
        static_context_functions[function.getEntryPoint().getOffset()] = function

for reference in currentProgram.getReferenceManager().getReferencesTo(AES_SETUP):
    function = manager.getFunctionContaining(reference.getFromAddress())
    if function:
        aes_setup_callers[function.getEntryPoint().getOffset()] = function

with open(r"C:\temp\buddy_sms_key.txt", "w") as output:
    output.write("=== instrucoes ===\n")
    for address, function, instruction in matches:
        name = function.getName() if function else "(sem funcao)"
        output.write("%s %-24s %s\n" % (address, name, instruction))

    output.write("\n=== funcoes ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))

    output.write("\n\n=== disassembly da derivacao de chave ===\n")
    key_function = manager.getFunctionAt(KEY_DERIVATION)
    for instruction in listing.getInstructions(key_function.getBody(), True):
        output.write("%s %s\n" % (instruction.getAddress(), instruction))

    output.write("\n=== credencial e callers da derivacao ===\n")
    for entry in sorted(credential_functions):
        function = credential_functions[entry]
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        for instruction in listing.getInstructions(function.getBody(), True):
            output.write("%s %s\n" % (instruction.getAddress(), instruction))

    output.write("\n=== contexto estatico da credencial ===\n")
    for entry in sorted(static_context_functions):
        function = static_context_functions[entry]
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))
        output.write("\n--- disassembly ---\n")
        for instruction in listing.getInstructions(function.getBody(), True):
            output.write("%s %s\n" % (instruction.getAddress(), instruction))

    output.write("\n=== callers do key setup AES ===\n")
    for entry in sorted(aes_setup_callers):
        function = aes_setup_callers[entry]
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), entry))
        output.write(code.encode("ascii", "replace").decode("ascii"))
        output.write("\n--- disassembly ---\n")
        for instruction in listing.getInstructions(function.getBody(), True):
            output.write("%s %s\n" % (instruction.getAddress(), instruction))

print("buddy sms key: %d instrucoes, %d funcoes" % (len(matches), len(functions)))
