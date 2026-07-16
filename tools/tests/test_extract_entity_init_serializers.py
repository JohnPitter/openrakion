import struct
import unittest

from tools.extract_entity_init_serializers import (
    APPLY_INIT_SLOT,
    GET_INIT_SLOT,
    SET_DEFAULT_PROPERTIES_SLOT,
    find_vtable_by_virtual,
    local_vtable_assignments,
    map_pe_image,
    parse_objdump_exports,
    resolve_export_factory,
    resolve_vtable,
)


class ExtractEntityInitSerializersTests(unittest.TestCase):
    @staticmethod
    def write_vtable(memory, base, vtable):
        rva = vtable - base
        struct.pack_into("<I", memory, rva + GET_INIT_SLOT, base + 0x20)
        struct.pack_into("<I", memory, rva + APPLY_INIT_SLOT, base + 0x30)

    def test_parses_matching_export_rows(self):
        output = """
\t[12] +base[13] 00123456 Export RVA
\t[12] +base[13]  000c CNpcNak1_DLLClass
"""
        self.assertEqual({"CNpcNak1_DLLClass": 0x123456}, parse_objdump_exports(output))

    def test_maps_pe32_sections_to_virtual_addresses(self):
        raw = bytearray(0x240)
        raw[:2] = b"MZ"
        struct.pack_into("<I", raw, 0x3C, 0x80)
        raw[0x80:0x84] = b"PE\0\0"
        struct.pack_into("<H", raw, 0x86, 1)
        struct.pack_into("<H", raw, 0x94, 0xE0)
        optional = 0x98
        struct.pack_into("<H", raw, optional, 0x10B)
        struct.pack_into("<I", raw, optional + 28, 0x35000000)
        struct.pack_into("<I", raw, optional + 56, 0x3000)
        struct.pack_into("<I", raw, optional + 60, 0x200)
        section = optional + 0xE0
        struct.pack_into("<IIII", raw, section + 8, 4, 0x1000, 4, 0x200)
        raw[0x200:0x204] = b"TEST"

        base, memory = map_pe_image(bytes(raw))

        self.assertEqual(0x35000000, base)
        self.assertEqual(0x3000, len(memory))
        self.assertEqual(b"TEST", memory[0x1000:0x1004])

    def test_rejects_non_pe_input(self):
        with self.assertRaisesRegex(ValueError, "imagem PE"):
            map_pe_image(b"not a pe")

    def test_resolves_vtable_through_factory_call(self):
        base = 0x35000000
        memory = bytearray(0x500)
        factory_rva = 0x40
        constructor_rva = 0x180
        displacement = constructor_rva - (factory_rva + 5)
        memory[factory_rva:factory_rva + 5] = b"\xE8" + struct.pack("<i", displacement)
        vtable = base + 0x300
        memory[constructor_rva:constructor_rva + 6] = b"\xC7\x01" + struct.pack("<I", vtable)
        self.write_vtable(memory, base, vtable)
        self.assertEqual(vtable, resolve_vtable(bytes(memory), base + factory_rva, base))

    def test_prefers_derived_assignment_in_factory(self):
        base = 0x35000000
        memory = bytearray(0x500)
        factory_rva = 0x40
        base_constructor_rva = 0x180
        displacement = base_constructor_rva - (factory_rva + 5)
        derived_vtable = base + 0x320
        memory[factory_rva:factory_rva + 11] = (
            b"\xE8" + struct.pack("<i", displacement) +
            b"\xC7\x00" + struct.pack("<I", derived_vtable))
        memory[base_constructor_rva:base_constructor_rva + 6] = (
            b"\xC7\x01" + struct.pack("<I", base + 0x300))
        self.write_vtable(memory, base, base + 0x300)
        self.write_vtable(memory, base, derived_vtable)
        self.assertEqual([derived_vtable], local_vtable_assignments(
            bytes(memory), base + factory_rva, base))
        self.assertEqual(derived_vtable, resolve_vtable(
            bytes(memory), base + factory_rva, base))

    def test_accepts_vtable_assignment_through_esi(self):
        base = 0x35000000
        memory = bytearray(0x500)
        function_rva = 0x40
        vtable = base + 0x300
        memory[function_rva:function_rva + 6] = b"\xC7\x06" + struct.pack("<I", vtable)
        self.write_vtable(memory, base, vtable)
        self.assertEqual([vtable], local_vtable_assignments(
            bytes(memory), base + function_rva, base))

    def test_rejects_module_data_that_is_not_a_vtable(self):
        base = 0x35000000
        memory = bytearray(0x500)
        function_rva = 0x40
        memory[function_rva:function_rva + 6] = (
            b"\xC7\x07" + struct.pack("<I", base + 0x300))
        self.assertEqual([], local_vtable_assignments(
            bytes(memory), base + function_rva, base))

    def test_resolves_aggregate_dllclass_factory(self):
        base = 0x35000000
        memory = bytearray(0x700)
        export_rva = 0x500
        descriptor_rva = 0x400
        factory_rva = 0x80
        vtable = base + 0x300
        struct.pack_into("<I", memory, export_rva, base + descriptor_rva)
        struct.pack_into("<I", memory, descriptor_rva - 4, base + factory_rva)
        memory[factory_rva:factory_rva + 6] = b"\xC7\x06" + struct.pack("<I", vtable)
        self.write_vtable(memory, base, vtable)
        self.assertEqual((base + factory_rva, vtable), resolve_export_factory(
            bytes(memory), export_rva, base))

    def test_finds_primary_vtable_from_exported_virtual_method(self):
        base = 0x35000000
        memory = bytearray(0x700)
        vtable = base + 0x300
        method = base + 0x80
        struct.pack_into("<I", memory, 0x300 + SET_DEFAULT_PROPERTIES_SLOT, method)
        self.write_vtable(memory, base, vtable)
        self.assertEqual(vtable, find_vtable_by_virtual(
            bytes(memory), method, SET_DEFAULT_PROPERTIES_SLOT, base))


if __name__ == "__main__":
    unittest.main()
