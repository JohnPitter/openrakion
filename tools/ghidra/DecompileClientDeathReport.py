# Extrai a selecao do cause de morte e a chamada SendFieldGameDiePlayer.
# @category Rakion

OUTPUT = r"C:\temp\client_death_report.txt"
DEATH_START = 0x3515E830
PRODUCER_START = 0x3515E8DE
PRODUCER_END = 0x3515E9DF
EPILOGUE_START = 0x35160423
EPILOGUE_END = 0x35160447
GOLD_GOLEM_NAME = 0x352D0A0C
KILL_TYPE_TABLE = 0x3538D5C0

listing = currentProgram.getListing()
memory = currentProgram.getMemory()


def write_range(output, start_value, end_value):
    start = toAddr(start_value)
    end = toAddr(end_value)
    instruction = listing.getInstructionAt(start)
    if instruction is None:
        instruction = listing.getInstructionAfter(start)
    while instruction is not None and instruction.getAddress().compareTo(end) <= 0:
        output.write("%s | %s\n" % (instruction.getAddress(), instruction))
        instruction = instruction.getNext()


def read_ascii(address_value):
    chars = []
    address = toAddr(address_value)
    for offset in range(128):
        value = memory.getByte(address.add(offset)) & 0xff
        if value == 0:
            break
        chars.append(chr(value))
    return "".join(chars)


with open(OUTPUT, "w") as output:
    output.write("program=%s\n" % currentProgram.getName())
    output.write("death=0x%08X\n" % DEATH_START)
    output.write("gold_golem_name=%s\n" % read_ascii(GOLD_GOLEM_NAME))
    output.write("kill_type_table_values=")
    values = []
    for index in range(7):
        values.append(str(memory.getInt(toAddr(KILL_TYPE_TABLE + index * 8))))
    output.write(",".join(values) + "\n")
    output.write("\n===== cause producer and world send =====\n")
    write_range(output, PRODUCER_START, PRODUCER_END)
    output.write("\n===== by-value EPlayerDeath epilogue =====\n")
    write_range(output, EPILOGUE_START, EPILOGUE_END)

print("reporte de morte do cliente extraido em " + OUTPUT)
