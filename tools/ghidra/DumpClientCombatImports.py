# -*- coding: utf-8 -*-
# Lista a IAT usada pelas rotinas de combate de entitiesmp.dll.
# @category Rakion


START = 0x352B3000
END = 0x352B3D00
OUTPUT = r"C:\temp\entitiesmp_combat_imports.txt"

symbols = currentProgram.getSymbolTable()
references = currentProgram.getReferenceManager()
listing = currentProgram.getListing()

with open(OUTPUT, "w") as output:
    address = toAddr(START)
    while address.compareTo(toAddr(END)) < 0:
        symbol = symbols.getPrimarySymbol(address)
        refs = list(references.getReferencesFrom(address))
        data = listing.getDataAt(address)
        if symbol or refs or data:
            output.write("%s symbol=%s data=%s\n" % (
                address,
                symbol.getName(True) if symbol else "-",
                data.toString() if data else "-"))
            for reference in refs:
                target_symbol = symbols.getPrimarySymbol(reference.getToAddress())
                output.write("  -> %s %s type=%s\n" % (
                    reference.getToAddress(),
                    target_symbol.getName(True) if target_symbol else "-",
                    reference.getReferenceType()))
        address = address.add(4)

print("IAT de combate extraida em " + OUTPUT)
