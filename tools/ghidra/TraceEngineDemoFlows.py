# -*- coding: utf-8 -*-
# Localiza strings, simbolos e call sites de gravacao/reproducao .dem no modulo atual.
# Saida: C:\temp\client_demo_flows_<modulo>.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TOKENS = (
    "startdem", "stopdem", "demorec", "demoplay", "demotick",
    "demodata", "demotraffic", "demoquality", "recorded-", ".dem",
    "already recording", "cannot play demo", "error while playing demo",
    "demo tick", "recorded tick", "etrsdemo",
)
SHELL_SITES = (0x360EE6B0, 0x360EE6C0)
CONTROL_GLOBALS = (0x362BA794, 0x362BA798, 0x362BA79C)
CHUNK_CONSTANTS = (
    0x36227720, 0x36227728, 0x362276E0, 0x362276E8,
    0x36229A8C, 0x36229A94, 0x3622A094, 0x3622A0C4, 0x3622A0CC,
)

fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
strings = []
matched_symbols = []
functions = {}
call_sites = []

for address in SHELL_SITES:
    function = fm.getFunctionContaining(toAddr(address))
    if function:
        functions[function.getEntryPoint().getOffset()] = function

for address in CONTROL_GLOBALS:
    for reference in rm.getReferencesTo(toAddr(address)):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller
            call_sites.append((reference.getFromAddress(),
                               symbols.getPrimarySymbol(toAddr(address)), caller))

for data in listing.getDefinedData(True):
    if not data.hasStringValue():
        continue
    value = str(data.getValue())
    if not any(token in value.lower() for token in TOKENS):
        continue
    references = list(rm.getReferencesTo(data.getAddress()))
    strings.append((data.getAddress(), value, references))
    for reference in references:
        caller = fm.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True)
    if not any(token in name.lower() for token in TOKENS):
        continue
    matched_symbols.append(symbol)
    function = fm.getFunctionAt(symbol.getAddress())
    if function:
        functions[function.getEntryPoint().getOffset()] = function
    for reference in rm.getReferencesTo(symbol.getAddress()):
        caller = fm.getFunctionContaining(reference.getFromAddress())
        call_sites.append((reference.getFromAddress(), symbol, caller))
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

frontier = list(functions.values())
for _ in range(2):
    parents = []
    for function in frontier:
        for reference in rm.getReferencesTo(function.getEntryPoint()):
            caller = fm.getFunctionContaining(reference.getFromAddress())
            if caller and caller.getEntryPoint().getOffset() not in functions:
                functions[caller.getEntryPoint().getOffset()] = caller
                parents.append(caller)
    frontier = parents

module = currentProgram.getName().lower().replace(".dll", "").replace(".bin", "").replace(".exe", "")
output_path = r"C:\temp\client_demo_flows_%s.txt" % module
with open(output_path, "w") as output:
    output.write("=== chunk constants ===\n")
    memory = currentProgram.getMemory()
    for address in CHUNK_CONSTANTS:
        raw = bytearray(5)
        memory.getBytes(toAddr(address), raw)
        output.write("%08X hex=%s ascii=%s\n" % (
            address, "".join("%02X" % (value & 0xff) for value in raw),
            "".join(chr(value & 0xff) if 32 <= (value & 0xff) < 127 else "." for value in raw)))
    output.write("\n")
    output.write("=== strings ===\n")
    for address, value, references in strings:
        output.write("%s %s\n" % (address, value.encode("ascii", "replace").decode("ascii")))
        for reference in references:
            caller = fm.getFunctionContaining(reference.getFromAddress())
            output.write("  ref=%s caller=%s instruction=%s\n" % (
                reference.getFromAddress(), caller.getName(True) if caller else "-",
                listing.getInstructionAt(reference.getFromAddress()) or "-"))
    output.write("\n=== symbols ===\n")
    for symbol in matched_symbols:
        output.write("%s %s type=%s\n" % (symbol.getAddress(), symbol.getName(True), symbol.getSymbolType()))
    output.write("\n=== call sites ===\n")
    for address, symbol, caller in call_sites:
        output.write("%s target=%s caller=%s instruction=%s\n" % (
            address, symbol.getName(True) if symbol else "-", caller.getName(True) if caller else "-",
            listing.getInstructionAt(address) or "-"))
    output.write("\n=== decompiled ===\n")
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 180, monitor)
        code = (result.getDecompiledFunction().getC()
                if result and result.getDecompiledFunction() else "(falha)")
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("Demo flows: %d strings, %d symbols, %d call sites, %d funcoes, saida=%s" %
      (len(strings), len(matched_symbols), len(call_sites), len(functions), output_path))
