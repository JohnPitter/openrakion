# Imprime instrucoes de intervalos VA no formato inicio:fim.
# @category Rakion

listing = currentProgram.getListing()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()

for raw in getScriptArgs():
    parts = raw.split(":", 1)
    if len(parts) != 2:
        raise ValueError("uso: DumpInstructionRanges.py <inicio:fim> [...]")
    start = space.getAddress(int(parts[0], 0))
    end = space.getAddress(int(parts[1], 0))
    print("\n===== %s:%s =====" % (start, end))
    instruction = listing.getInstructionAt(start)
    if instruction is None:
        instruction = listing.getInstructionAfter(start)
    while instruction is not None and instruction.getAddress().compareTo(end) <= 0:
        print("%s  %s" % (instruction.getAddress(), instruction))
        instruction = instruction.getNext()

print("##### DONE #####")
