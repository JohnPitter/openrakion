# Exibe todas as instrucoes das funcoes que contem os enderecos informados.
# @category Rakion

listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()

print("PROGRAM=%s" % currentProgram.getName())
for raw in getScriptArgs():
    address = space.getAddress(int(raw, 0))
    function = functions.getFunctionContaining(address)
    if function is None:
        print("\nADDRESS=%s sem funcao" % address)
        continue
    print("\n===== %s @ %s =====" % (function.getName(), function.getEntryPoint()))
    instructions = listing.getInstructions(function.getBody(), True)
    while instructions.hasNext():
        instruction = instructions.next()
        print("%s | %s" % (instruction.getAddress(), instruction))

print("##### DONE #####")
