# -*- coding: utf-8 -*-
# Rastreia as entradas da tabela de consumidores S->C proximos ao voto.
# Saida: C:\temp\client_field_vote_consumer_table.txt
# @category Rakion

ADDRESSES = (0x36193c50, 0x36193c80, 0x36193ca0, 0x36193cc0, 0x36193cf0,
             0x36193d10, 0x36193d70, 0x36193de0, 0x36193e40, 0x36193e80,
             0x36193ed0, 0x36193ee0, 0x36193f40)
references = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
functions = currentProgram.getFunctionManager()

with open(r"C:\temp\client_field_vote_consumer_table.txt", "w") as output:
    for target in ADDRESSES:
        output.write("\n===== target %08x =====\n" % target)
        iterator = references.getReferencesTo(toAddr(target))
        while iterator.hasNext():
            reference = iterator.next()
            source = reference.getFromAddress()
            instruction = listing.getInstructionAt(source)
            function = functions.getFunctionContaining(source)
            output.write("from=%s type=%s instruction=%s function=%s\n" %
                         (source, reference.getReferenceType(), instruction,
                          function.getName(True) if function else "-"))

print("referencias da tabela de consumidores extraidas")
