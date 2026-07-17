# -*- coding: utf-8 -*-
# Censo completo de descritores de classe de entidade na build v258.
# Um descritor concreto tem o layout [name*, binder, class_id, parent_def*, factory*, ...]
# e vem precedido do registro [props*, propCount, events*, eventCount, comps*, compCount].
# @category Rakion
memory = currentProgram.getMemory()
space = currentProgram.getAddressFactory().getDefaultAddressSpace()

args = getScriptArgs()
output_path = args[0] if args else r"C:\temp\entity_class_census.txt"

BINDER = 0x352B42D5
REGION_START = 0x35380000
REGION_END = 0x353A8000


def u32(addr):
    return memory.getInt(space.getAddress(addr)) & 0xffffffff


def ascii_at(addr, limit=96):
    chars = []
    cursor = space.getAddress(addr)
    try:
        for _ in range(limit):
            value = memory.getByte(cursor) & 0xff
            if value == 0:
                break
            if not (0x20 <= value < 0x7f):
                return None
            chars.append(chr(value))
            cursor = cursor.add(1)
    except Exception:
        return None
    return "".join(chars) if chars else None


records = []
by_def_addr = {}
for addr in range(REGION_START, REGION_END, 4):
    try:
        if u32(addr + 4) != BINDER:
            continue
        name_ptr = u32(addr)
    except Exception:
        continue
    if not (0x35280000 <= name_ptr < 0x35380000):
        continue
    name = ascii_at(name_ptr)
    if not name or len(name) < 3:
        continue
    class_id = u32(addr + 8)
    if class_id == 0 or class_id > 0xffffff:
        continue
    parent_def = u32(addr + 12)
    factory = u32(addr + 16)
    events_ptr = u32(addr - 8)
    event_count = u32(addr - 4)
    if not (0x35380000 <= events_ptr < REGION_END) or event_count > 0x200:
        events_ptr, event_count = 0, 0
    record = {
        "addr": addr,
        "name": name,
        "class_id": class_id,
        "parent_def": parent_def,
        "factory": factory,
        "events": events_ptr,
        "event_count": event_count,
    }
    records.append(record)
    by_def_addr[addr] = record

out = open(output_path, "w")
out.write("PROGRAM=%s TOTAL=%d\n" % (currentProgram.getName(), len(records)))
out.write("addr name class_id parent(resolvido) factory events count\n")
for record in sorted(records, key=lambda item: item["class_id"]):
    parent = by_def_addr.get(record["parent_def"])
    parent_name = parent["name"] if parent else ("engine:%08x" % record["parent_def"])
    out.write("%08x %-28s %08x %-28s %08x %08x %3d\n" % (
        record["addr"], record["name"], record["class_id"], parent_name,
        record["factory"], record["events"], record["event_count"]))
out.close()
print("censo com %d descritores em %s" % (len(records), output_path))
