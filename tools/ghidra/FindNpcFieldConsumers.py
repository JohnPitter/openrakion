# Localiza funcoes candidatas que indexam registros NPC de 0xA0 bytes e o campo 0x8C/0x90.
# @category Rakion

listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()

print("PROGRAM=%s" % currentProgram.getName())
candidates = []
iterator = functions.getFunctions(True)
while iterator.hasNext():
    function = iterator.next()
    has_stride = False
    field_hits = []
    instructions = listing.getInstructions(function.getBody(), True)
    while instructions.hasNext():
        instruction = instructions.next()
        rendered = instruction.toString().lower()
        if "0xa0" in rendered:
            has_stride = True
        if "0x8c" in rendered or "0x90" in rendered:
            field_hits.append((instruction.getAddress(), instruction.toString()))
    if has_stride and field_hits:
        candidates.append((function, field_hits))

print("CANDIDATE_COUNT=%d" % len(candidates))
for function, field_hits in candidates:
    print("\n%s @ %s" % (function.getName(), function.getEntryPoint()))
    for address, rendered in field_hits:
        print("  %s | %s" % (address, rendered))

print("##### DONE #####")
