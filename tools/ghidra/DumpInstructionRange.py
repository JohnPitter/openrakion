# Exibe as instrucoes contidas no intervalo inclusivo informado.
# @category Rakion

listing = currentProgram.getListing()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()
arguments = getScriptArgs()

if len(arguments) != 2:
    raise ValueError("uso: DumpInstructionRange.py <inicio> <fim>")

start = space.getAddress(int(arguments[0], 0))
end = space.getAddress(int(arguments[1], 0))
instruction = listing.getInstructionAt(start)
if instruction is None:
    instruction = listing.getInstructionAfter(start)

print("PROGRAM=%s RANGE=%s..%s" % (currentProgram.getName(), start, end))
while instruction is not None and instruction.getAddress().compareTo(end) <= 0:
    print("%s | %s" % (instruction.getAddress(), instruction))
    instruction = instruction.getNext()

print("##### DONE #####")
