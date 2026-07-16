# -*- coding: utf-8 -*-
# Resolve no engine.dll os alvos importados pelas rotinas de combate.
# @category Rakion


TARGETS = (
    0x360049A0, 0x360048C0, 0x36004910, 0x360048A0, 0x362ACC78,
    0x36128A90, 0x361986E0, 0x3600CFC0, 0x36004B80, 0x3636F260,
    0x3611F8F0, 0x36100CD0, 0x36100BF0,
)
OUTPUT = r"C:\temp\engine_combat_targets.txt"

symbols = currentProgram.getSymbolTable()
manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()

with open(OUTPUT, "w") as output:
    for target in TARGETS:
        address = toAddr(target)
        symbol = symbols.getPrimarySymbol(address)
        function = manager.getFunctionAt(address)
        output.write("%s symbol=%s function=%s\n" % (
            address,
            symbol.getName(True) if symbol else "-",
            function.getName(True) if function else "-"))
        for reference in references.getReferencesTo(address):
            caller = manager.getFunctionContaining(reference.getFromAddress())
            output.write("  <- %s %s\n" % (
                reference.getFromAddress(), caller.getName(True) if caller else "-"))

print("alvos de combate do engine resolvidos em " + OUTPUT)
