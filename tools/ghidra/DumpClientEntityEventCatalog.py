# -*- coding: utf-8 -*-
# Extrai tamanho total e event id dos exports E* de entitiesmp.dll.
# @category Rakion
import struct


BASE_CONSTRUCTOR_CALL = [0xff, 0x15, 0x24, 0x30, 0x2b, 0x35]


def read_bytes(address, count):
    result = []
    for offset in range(count):
        result.append(currentProgram.getMemory().getByte(address.add(offset)) & 0xff)
    return result


def u32(values, offset):
    return (values[offset] | values[offset + 1] << 8 |
            values[offset + 2] << 16 | values[offset + 3] << 24)


def extract_size(address):
    values = read_bytes(address, 12)
    for offset in range(0, 7):
        if values[offset] == 0xb8 and values[offset + 5] == 0xc3:
            return u32(values, offset + 1)
    return None


def extract_event_id(address):
    values = read_bytes(address, 64)
    call_offset = -1
    for offset in range(0, len(values) - len(BASE_CONSTRUCTOR_CALL) + 1):
        if values[offset:offset + len(BASE_CONSTRUCTOR_CALL)] == BASE_CONSTRUCTOR_CALL:
            call_offset = offset
            break
    if call_offset < 0:
        return None
    for offset in range(call_offset - 2, max(-1, call_offset - 40), -1):
        if values[offset] == 0x6a:
            return values[offset + 1]
        if values[offset] == 0x68:
            return u32(values, offset + 1)
    return None


arguments = getScriptArgs()
if len(arguments) != 2:
    raise ValueError("uso: DumpClientEntityEventCatalog.py <input.tsv> <output.tsv>")

with open(arguments[0], "r") as source:
    rows = [line.rstrip("\r\n").split("\t") for line in source if line.strip()]

with open(arguments[1], "w") as output:
    output.write("name\tget_size_va\tconstructor_va\ttotal_size\tevent_id\tstatus\n")
    for name, get_size_raw, constructor_raw in rows[1:]:
        get_size_va = int(get_size_raw, 0)
        constructor_va = None if constructor_raw == "-" else int(constructor_raw, 0)
        total_size = extract_size(toAddr(get_size_va))
        event_id = extract_event_id(toAddr(constructor_va)) if constructor_va else None
        status = "ok" if total_size is not None and event_id is not None else "unresolved"
        output.write("%s\t0x%08X\t%s\t%s\t%s\t%s\n" % (
            name,
            get_size_va,
            "0x%08X" % constructor_va if constructor_va is not None else "-",
            "0x%X" % total_size if total_size is not None else "-",
            "0x%08X" % event_id if event_id is not None else "-",
            status,
        ))

print("catalogo runtime de eventos gravado em " + arguments[1])
