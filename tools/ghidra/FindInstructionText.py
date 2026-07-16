# Lista funcoes com instrucoes que contem fragmentos textuais informados.
# @category Rakion

listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()
tokens = tuple(raw.lower() for raw in getScriptArgs())
hits = {}

for instruction in listing.getInstructions(True):
    rendered = instruction.toString().lower()
    if not any(token in rendered for token in tokens):
        continue
    function = functions.getFunctionContaining(instruction.getAddress())
    if function is None:
        continue
    entry = function.getEntryPoint().getOffset()
    hits.setdefault(entry, (function, []))[1].append(instruction)

print("PROGRAM=%s TOKENS=%s FUNCTIONS=%d" % (
    currentProgram.getName(), ",".join(tokens), len(hits)))
for entry in sorted(hits):
    function, instructions = hits[entry]
    print("\n%s @ %s" % (function.getName(), function.getEntryPoint()))
    for instruction in instructions:
        print("  %s | %s" % (instruction.getAddress(), instruction))

print("##### DONE #####")
