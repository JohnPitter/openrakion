# Finds indirect CALL instructions whose operand contains a requested displacement.
# @category Rakion.RE


arguments = getScriptArgs()
if len(arguments) != 1:
    printerr("Uso: FindVirtualCallOffset.py <offset>")
    exit()

offset = int(arguments[0], 0)
listing = currentProgram.getListing()
function_manager = currentProgram.getFunctionManager()
matches = []

instruction = listing.getInstructions(True)
while instruction.hasNext():
    current = instruction.next()
    if current.getMnemonicString().upper() != "CALL":
        continue

    scalars = []
    for operand_index in range(current.getNumOperands()):
        for representation in current.getOpObjects(operand_index):
            if hasattr(representation, "getUnsignedValue"):
                scalars.append(representation.getUnsignedValue())

    if offset not in scalars:
        continue

    function = function_manager.getFunctionContaining(current.getAddress())
    matches.append((current, function))

println("PROGRAM=%s OFFSET=0x%x MATCHES=%d" % (
    currentProgram.getName(),
    offset,
    len(matches),
))

for current, function in matches:
    function_name = function.getName() if function is not None else "<none>"
    println("%s function=%s instruction=%s" % (
        current.getAddress(),
        function_name,
        current,
    ))

println("##### DONE #####")
