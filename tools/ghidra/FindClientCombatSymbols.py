# -*- coding: utf-8 -*-
# Inventaria simbolos e xrefs de HP, morte, respawn e invencibilidade.
# @category Rakion
import os


KEYWORDS = (
    "HP", "Damage", "Hit", "Dead", "Die", "Death", "Respawn", "Revive",
    "Alive", "Spawn", "Invinc", "Invulner", "Armor", "Armour", "Reduce", "Attack",
)

symbols = currentProgram.getSymbolTable()
references = currentProgram.getReferenceManager()
manager = currentProgram.getFunctionManager()
program_name = currentProgram.getName().replace(".", "_")
output_path = os.path.join(r"C:\temp", program_name + "_combat_symbols.txt")

rows = []
seen = set()
for symbol in symbols.getAllSymbols(True):
    name = symbol.getName()
    if not any(keyword.lower() in name.lower() for keyword in KEYWORDS):
        continue
    key = "%s@%s" % (name, symbol.getAddress())
    if key in seen:
        continue
    seen.add(key)
    refs = list(references.getReferencesTo(symbol.getAddress()))
    rows.append("%s @ %s refs=%d\n" % (name, symbol.getAddress(), len(refs)))
    for reference in refs[:40]:
        caller = manager.getFunctionContaining(reference.getFromAddress())
        rows.append("  <- %s %s @ %s\n" % (
            reference.getFromAddress(),
            caller.getName() if caller else "(sem funcao)",
            caller.getEntryPoint() if caller else "-"))

with open(output_path, "w") as output:
    output.writelines(rows)

print("simbolos de combate inventariados em " + output_path)
