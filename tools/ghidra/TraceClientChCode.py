# -*- coding: utf-8 -*-
# Rastreia referências ao SendChCode/SendPacketSpeedTest e possíveis calls virtuais.
# Saida: C:\temp\client_ch_code_refs.txt
# @category Rakion
fm = currentProgram.getFunctionManager()
rm = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
symbols = currentProgram.getSymbolTable()
targets = []

for symbol in symbols.getAllSymbols(True):
    name = symbol.getName(True).lower()
    if "sendchcode" in name or "sendpacketspeedtest" in name:
        targets.append((symbol.getAddress(), symbol.getName(True)))

with open(r"C:\temp\client_ch_code_refs.txt", "w") as output:
    output.write("=== related symbols ===\n")
    for symbol in symbols.getAllSymbols(True):
        name = symbol.getName(True).lower()
        if "iscavengerworldnet" in name and ("vftable" in name or "vtable" in name):
            output.write("%s %s\n" % (symbol.getAddress(), symbol.getName(True)))
    for address, name in targets:
        output.write("\n=== %s @ %s ===\n" % (name, address))
        for reference in rm.getReferencesTo(address):
            source = reference.getFromAddress()
            function = fm.getFunctionContaining(source)
            source_symbol = symbols.getPrimarySymbol(source)
            output.write("ref %s type=%s function=%s symbol=%s\n" % (
                source, reference.getReferenceType(),
                function.getName(True) if function else "-",
                source_symbol.getName(True) if source_symbol else "-"))
            instruction = listing.getInstructionAt(source)
            if instruction:
                output.write("  %s\n" % instruction)
            thunk_callers = list(rm.getReferencesTo(source))
            if instruction and instruction.getMnemonicString() == "JMP":
                output.write("  thunk_callers=%d\n" % len(thunk_callers))
                for thunk_reference in thunk_callers:
                    thunk_source = thunk_reference.getFromAddress()
                    thunk_function = fm.getFunctionContaining(thunk_source)
                    output.write("    ref=%s type=%s function=%s instruction=%s\n" % (
                        thunk_source, thunk_reference.getReferenceType(),
                        thunk_function.getName(True) if thunk_function else "-",
                        listing.getInstructionAt(thunk_source) or "-"))

print("Client ChCode refs: %d alvos" % len(targets))
