# Dump the exact instructions around the client-side bot hit candidate sites.

OUTPUT = r"C:\temp\client_bot_hit_sites.txt"
RANGES = (
    (0x35152DA0, 0x35152E30, "ReceiveDamage entry"),
    (0x35153380, 0x35153430, "ReceiveDamage post-validation"),
    (0x3517D3D0, 0x3517D550, "UpdateWeaponHit"),
    (0x35153C80, 0x35153D40, "AddHitCount"),
    (0x35153CE0, 0x35153D80, "AddHitCount entry"),
)


listing = currentProgram.getListing()

with open(OUTPUT, "w") as output:
    for start, end, label in RANGES:
        output.write("\n===== %s =====\n" % label)
        instruction = listing.getInstructionContaining(toAddr(start))
        if instruction is None:
            disassemble(toAddr(start))
            instruction = listing.getInstructionContaining(toAddr(start))
        while instruction is not None and instruction.getAddress().getOffset() < end:
            bytes_text = " ".join("%02x" % (value & 0xff) for value in instruction.getBytes())
            output.write("%s  %-24s  %s\n" % (
                instruction.getAddress(), bytes_text, instruction.toString()))
            instruction = instruction.getNext()

    output.write("\n===== ReceiveDamage callers =====\n")
    references = currentProgram.getReferenceManager().getReferencesTo(toAddr(0x35152DA0))
    for reference in references:
        call = listing.getInstructionContaining(reference.getFromAddress())
        if call is None:
            continue
        instruction = call
        for _ in range(12):
            previous = instruction.getPrevious()
            if previous is None:
                break
            instruction = previous
        output.write("\n--- caller at %s ---\n" % reference.getFromAddress())
        for _ in range(25):
            if instruction is None:
                break
            bytes_text = " ".join("%02x" % (value & 0xff) for value in instruction.getBytes())
            output.write("%s  %-24s  %s\n" % (
                instruction.getAddress(), bytes_text, instruction.toString()))
            instruction = instruction.getNext()

print("sites de hit do cliente extraidos em " + OUTPUT)
