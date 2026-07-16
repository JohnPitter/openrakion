# Localiza chamadas virtuais diretas ou materializadas por MOV para um slot.
# @category Rakion

listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()
slot = int(getScriptArgs()[0], 0)
tokens = ("+ 0x%x]" % slot, "+0x%x]" % slot)
hits = []


def contains_slot(instruction):
    rendered = instruction.toString().lower()
    return any(token in rendered for token in tokens)


for instruction in listing.getInstructions(True):
    if not contains_slot(instruction):
        continue
    function = functions.getFunctionContaining(instruction.getAddress())
    if function is None:
        continue
    sequence = [instruction]
    cursor = instruction
    matched = instruction.getMnemonicString().upper() == "CALL"
    for _ in range(8):
        cursor = cursor.getNext()
        if cursor is None or not function.getBody().contains(cursor.getAddress()):
            break
        sequence.append(cursor)
        if cursor.getMnemonicString().upper() == "CALL":
            matched = True
            break
    if matched:
        hits.append((function, sequence))

print("PROGRAM=%s SLOT=0x%x HITS=%d" % (currentProgram.getName(), slot, len(hits)))
for function, sequence in hits:
    print("\n%s @ %s" % (function.getName(), function.getEntryPoint()))
    for instruction in sequence:
        print("  %s | %s" % (instruction.getAddress(), instruction))

print("##### DONE #####")
