# -*- coding: utf-8 -*-
import struct

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


NPC_BASE_EVENT_TABLE = 0x35389FB8
NPC_BASE_EVENT_COUNT = 102


def _bits_to_float(value):
    return struct.unpack("<f", struct.pack("<I", value))[0]


class NpcFamilyExtractor(object):
    def __init__(self, env, config):
        self.env = env
        self.config = config
        self.program = env["currentProgram"]
        self.to_addr = env["toAddr"]
        self.memory = self.program.getMemory()
        self.manager = self.program.getFunctionManager()
        self.listing = self.program.getListing()
        self.decompiler = DecompInterface()
        self.decompiler.openProgram(self.program)
        self.monitor = ConsoleTaskMonitor()

    def u32(self, address):
        return self.memory.getInt(self.to_addr(address)) & 0xffffffff

    def ascii_string(self, address, limit=160):
        chars = []
        cursor = self.to_addr(address)
        for _ in range(limit):
            value = self.memory.getByte(cursor) & 0xff
            if value == 0:
                break
            chars.append(chr(value) if 0x20 <= value < 0x7f else "?")
            cursor = cursor.add(1)
        return "".join(chars)

    def event_record(self, address):
        return tuple(self.u32(address + offset) for offset in (0, 4, 8, 12))

    def decompile(self, output, address):
        target = self.to_addr(address)
        function = self.manager.getFunctionContaining(target)
        if function is None and self.memory.contains(target):
            self.env["disassemble"](target)
            self.env["createFunction"](target, None)
            function = self.manager.getFunctionContaining(target)
        if function is None:
            output.write("\n===== 0x%08x ausente =====\n" % address)
            return
        output.write("\n===== %s @ %s =====\n" % (
            function.getName(), function.getEntryPoint()))
        result = self.decompiler.decompileFunction(function, 240, self.monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

    def disassemble_defaults(self, output):
        output.write("\n===== SetDefaultProperties entry points =====\n")
        for address in self.config["set_defaults"]:
            instruction = self.listing.getInstructionAt(self.to_addr(address))
            if instruction is None:
                self.env["disassemble"](self.to_addr(address))
                instruction = self.listing.getInstructionAt(self.to_addr(address))
            output.write("%08x" % address)
            for _ in range(4):
                if instruction is None:
                    break
                output.write(" | %s" % instruction)
                flow = instruction.getFlowType()
                if flow.isTerminal() or flow.isJump():
                    break
                instruction = instruction.getNext()
            output.write("\n")

    def collect_event_table(self, table, count, default_event):
        events = []
        mapped_ids = set()
        for index in range(count):
            record = self.event_record(table + index * 16)
            events.append(record)
            if record[1] != 0xffffffff:
                mapped_ids.add(record[1])
        events.append(self.event_record(default_event))
        return events, mapped_ids

    def collect_events(self):
        events, mapped_ids = self.collect_event_table(
            self.config["event_table"],
            self.config["event_count"],
            self.config["default_event"],
        )
        extra_tables = []
        for label, table, count, default_event in self.config.get(
                "extra_event_tables", ()):
            extra_events, extra_mapped = self.collect_event_table(
                table, count, default_event)
            extra_tables.append((label, extra_events))
            mapped_ids.update(extra_mapped)
        base_handlers = {}
        for index in range(NPC_BASE_EVENT_COUNT):
            record = self.event_record(NPC_BASE_EVENT_TABLE + index * 16)
            if record[0] in mapped_ids:
                base_handlers[record[0]] = record[2]
        return events, extra_tables, mapped_ids, base_handlers

    def write_event_table(self, output, title, events):
        output.write("\n===== %s local events =====\n" % title)
        for event_id, base_id, handler, binder in events[:-1]:
            output.write(
                "event=%08x base=%08x handler=%08x binder=%08x\n" % (
                    event_id, base_id, handler, binder))
        event_id, base_id, handler, binder = events[-1]
        output.write("\n===== %s default event =====\n" % title)
        output.write(
            "event=%08x base=%08x handler=%08x binder=%08x\n" % (
                event_id, base_id, handler, binder))

    def write(self, output_path):
        events, extra_tables, mapped_ids, base_handlers = self.collect_events()
        with open(output_path, "w") as output:
            output.write("PROGRAM=%s FAMILY=%s\n" % (
                self.program.getName(), self.config["name"]))
            output.write("\n===== variant descriptors =====\n")
            for address in self.config["descriptors"]:
                name_pointer = self.u32(address)
                output.write(
                    "%08x name=%s class_id=%08x parent=%08x factory=%08x\n" % (
                        address,
                        self.ascii_string(name_pointer),
                        self.u32(address + 8),
                        self.u32(address + 12),
                        self.u32(address + 16),
                    )
                )
            output.write("\n===== assets =====\n")
            for address in self.config["assets"]:
                output.write("%08x %s\n" % (address, self.ascii_string(address)))
            output.write("\n===== scalars =====\n")
            for label, bits in self.config.get("scalars", ()):
                output.write("%s bits=%08x value=%s\n" % (
                    label, bits, _bits_to_float(bits)))
            self.write_event_table(output, self.config["name"], events)
            for label, extra_events in extra_tables:
                self.write_event_table(output, label, extra_events)
            output.write("\n===== mapped CNpcBase handlers =====\n")
            for event_id in sorted(mapped_ids):
                output.write("event=%08x handler=%08x\n" % (
                    event_id, base_handlers.get(event_id, 0)))
            self.disassemble_defaults(output)
            all_events = list(events)
            for _, extra_events in extra_tables:
                all_events.extend(extra_events)
            handlers = sorted(set(record[2] for record in all_events))
            targets = handlers + list(self.config.get("helpers", ()))
            targets += list(base_handlers.values())
            for address in targets:
                self.decompile(output, address)


def extract_family(env, config):
    program = env["currentProgram"]
    if program.getName().lower() != "entitiesmp_dump.bin":
        raise ValueError("execute na imagem runtime entitiesmp_dump.bin da build v258")
    args = env["getScriptArgs"]()
    output_path = args[0] if args else config["default_output"]
    NpcFamilyExtractor(env, config).write(output_path)
    print("familia %s extraida em %s" % (config["name"], output_path))
