# Exibe instrucoes ao redor dos enderecos passados.
# @category Rakion

listing = currentProgram.getListing()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()

print("PROGRAM=%s" % currentProgram.getName())
for raw in getScriptArgs():
    address = space.getAddress(int(raw, 0))
    instruction = listing.getInstructionContaining(address)
    if instruction is None:
        print("\nCENTER=%s ausente" % address)
        continue
    cursor = instruction
    for _ in range(24):
        previous = cursor.getPrevious()
        if previous is None:
            break
        cursor = previous
    print("\nCENTER=%s" % address)
    for _ in range(56):
        marker = ">" if cursor.contains(address) else " "
        print("%s %s | %s" % (marker, cursor.getAddress(), cursor))
        cursor = cursor.getNext()
        if cursor is None:
            break

print("##### DONE #####")
